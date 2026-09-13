using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sayra.Backend.Application.Abstractions.Caching;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Application.OfflineQueue
{
    public class OfflineOrderingAndReconciliationEngine : IOfflineOrderingAndReconciliationEngine
    {
        private readonly IProcessedEventRepository _processedEventRepository;
        private readonly IWorkstationStreamStateRepository _streamStateRepository;
        private readonly IRepository<Workstation> _workstationRepository;
        private readonly IRepository<Site> _siteRepository;
        private readonly IRepository<Session> _sessionRepository;
        private readonly IRepository<AuditEvent> _auditEventRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly OfflineOrderingOptions _options;
        private readonly IRedisService? _redisService;
        private readonly IDeadLetterEventRepository? _deadLetterRepository;
        private readonly IFailureClassificationService? _failureClassifier;
        private readonly IOfflineBusinessReconciliationService? _businessReconciliationService;
        private readonly ILogger<OfflineOrderingAndReconciliationEngine> _logger;

        private static readonly SemaphoreSlim _concurrencyLock = new(1, 1);
        private const int MaxSinglePayloadBytes = 256 * 1024; // 256 KB

        public OfflineOrderingAndReconciliationEngine(
            IProcessedEventRepository processedEventRepository,
            IWorkstationStreamStateRepository streamStateRepository,
            IRepository<Workstation> workstationRepository,
            IRepository<Site> siteRepository,
            IRepository<Session> sessionRepository,
            IRepository<AuditEvent> auditEventRepository,
            IUnitOfWork unitOfWork,
            IOptions<OfflineOrderingOptions> options,
            ILogger<OfflineOrderingAndReconciliationEngine> logger,
            IRedisService? redisService = null,
            IDeadLetterEventRepository? deadLetterRepository = null,
            IFailureClassificationService? failureClassifier = null,
            IOfflineBusinessReconciliationService? businessReconciliationService = null)
        {
            _processedEventRepository = processedEventRepository ?? throw new ArgumentNullException(nameof(processedEventRepository));
            _streamStateRepository = streamStateRepository ?? throw new ArgumentNullException(nameof(streamStateRepository));
            _workstationRepository = workstationRepository ?? throw new ArgumentNullException(nameof(workstationRepository));
            _siteRepository = siteRepository ?? throw new ArgumentNullException(nameof(siteRepository));
            _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
            _auditEventRepository = auditEventRepository ?? throw new ArgumentNullException(nameof(auditEventRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _options = options?.Value ?? new OfflineOrderingOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _redisService = redisService;
            _deadLetterRepository = deadLetterRepository;
            _failureClassifier = failureClassifier;
            _businessReconciliationService = businessReconciliationService;
        }

        public async Task<OrderingAndReconciliationResult> EvaluateAndReconcileAsync(
            OfflineQueueItem item,
            string authenticatedPcId,
            string batchId,
            CancellationToken cancellationToken = default)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            await _concurrencyLock.WaitAsync(cancellationToken);
            try
            {
                return await EvaluateAndReconcileInternalAsync(item, authenticatedPcId, batchId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error evaluating event {EventId} in batch {BatchId}.", item.EventId, batchId);

                if (Guid.TryParse(item.EventId, out var eventGuid))
                {
                    try
                    {
                        var existing = await _processedEventRepository.GetByEventIdAsync(eventGuid, false, cancellationToken);
                        if (existing != null)
                        {
                            return CreateResult(item.EventId, OfflineOrderingStatus.Duplicate, OfflineReconciliationStatus.Duplicate,
                                OfflineReasonCode.StaleSequence, "Duplicate event processed concurrently.", isAcceptedForAck: true, existing);
                        }
                    }
                    catch
                    {
                        // Fallback
                    }
                }

                return CreateResult(item.EventId, OfflineOrderingStatus.Unordered, OfflineReconciliationStatus.Rejected,
                    OfflineReasonCode.MalformedPayload, ex.Message, isAcceptedForAck: false);
            }
            finally
            {
                _concurrencyLock.Release();
            }
        }

        private async Task<OrderingAndReconciliationResult> EvaluateAndReconcileInternalAsync(
            OfflineQueueItem item,
            string authenticatedPcId,
            string batchId,
            CancellationToken cancellationToken)
        {
            string authPcId = authenticatedPcId?.Trim().ToUpperInvariant() ?? string.Empty;

            // 1. Basic Envelope & Identity Mismatch Check
            if (string.IsNullOrEmpty(item.EventId) || !Guid.TryParse(item.EventId, out var eventGuid))
            {
                _logger.LogWarning("Rejecting offline event in batch {BatchId}: Invalid or missing EventId '{EventId}'.", batchId, item.EventId);
                var res = CreateResult(item.EventId ?? string.Empty, OfflineOrderingStatus.Unordered, OfflineReconciliationStatus.Rejected,
                    OfflineReasonCode.MalformedPayload, "Invalid or missing EventId.", isAcceptedForAck: false);
                await TryRecordDeadLetterAsync(item, authPcId, null, null, null, batchId, res.ReasonCode ?? "MALFORMED_PAYLOAD", res.ErrorMessage!, cancellationToken);
                return res;
            }

            // Extract payload & payload hash
            string payloadText = item.Payload is JsonElement elem ? elem.GetRawText() : (item.Payload?.ToString() ?? "{}");
            byte[] payloadBytes = Encoding.UTF8.GetBytes(payloadText);

            if (payloadBytes.Length > MaxSinglePayloadBytes)
            {
                _logger.LogWarning("Rejecting offline event {EventId} in batch {BatchId}: Payload exceeds 256 KB limit.", item.EventId, batchId);
                var res = CreateResult(item.EventId, OfflineOrderingStatus.Unordered, OfflineReconciliationStatus.Rejected,
                    OfflineReasonCode.MalformedPayload, "Payload exceeds 256 KB limit.", isAcceptedForAck: false);
                await TryRecordDeadLetterAsync(item, authPcId, null, null, null, batchId, res.ReasonCode ?? "MALFORMED_PAYLOAD", res.ErrorMessage!, cancellationToken);
                return res;
            }

            string payloadHash = ComputeSha256(payloadBytes);

            // Check if payload contains ClientId / WorkstationId that conflicts with authenticated identity
            string? payloadClientId = ExtractStringProperty(payloadText, "clientId") ?? ExtractStringProperty(payloadText, "workstationId");
            if (!string.IsNullOrEmpty(payloadClientId) && !payloadClientId.Trim().Equals(authPcId, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("SECURITY ALERT: Offline event {EventId} identity mismatch. Connection PcId={AuthPcId}, Payload identity={PayloadClientId}.",
                    item.EventId, authPcId, payloadClientId);

                var res = CreateResult(item.EventId, OfflineOrderingStatus.SequenceConflict, OfflineReconciliationStatus.Rejected,
                    OfflineReasonCode.IdentityMismatch, "Payload client identity does not match authenticated TCP session identity.", isAcceptedForAck: false);
                await TryRecordDeadLetterAsync(item, authPcId, null, null, null, batchId, res.ReasonCode ?? "IDENTITY_MISMATCH", res.ErrorMessage!, cancellationToken);
                return res;
            }

            // 2. Authoritative Workstation & Site Lookup
            var workstation = await _workstationRepository.FirstOrDefaultAsync(w => w.PcId == authPcId, false, cancellationToken);
            if (workstation == null)
            {
                _logger.LogWarning("CONFLICT ALERT: Offline event {EventId} references non-existent workstation PcId={AuthPcId}.", item.EventId, authPcId);
                var res = CreateResult(item.EventId, OfflineOrderingStatus.SequenceConflict, OfflineReconciliationStatus.Conflict,
                    OfflineReasonCode.WorkstationNotFound, "Workstation does not exist on authoritative server.", isAcceptedForAck: false);
                await TryRecordDeadLetterAsync(item, authPcId, null, null, null, batchId, res.ReasonCode ?? "WORKSTATION_NOT_FOUND", res.ErrorMessage!, cancellationToken);
                return res;
            }

            Site? site = null;
            if (!string.IsNullOrWhiteSpace(workstation.SiteId))
            {
                site = await _siteRepository.FirstOrDefaultAsync(s => s.SiteId == workstation.SiteId || s.Code == workstation.SiteId, false, cancellationToken);
                if (site != null && !site.CanOperate())
                {
                    _logger.LogWarning("CONFLICT ALERT: Offline event {EventId} references inactive site {SiteId} for workstation PcId={AuthPcId}.",
                        item.EventId, workstation.SiteId, authPcId);
                    var res = CreateResult(item.EventId, OfflineOrderingStatus.SequenceConflict, OfflineReconciliationStatus.Conflict,
                        OfflineReasonCode.SiteMismatch, "Workstation site is inactive.", isAcceptedForAck: false);
                    await TryRecordDeadLetterAsync(item, authPcId, workstation.Id, workstation.SiteId, site.OrganizationId, batchId, res.ReasonCode ?? "SITE_MISMATCH", res.ErrorMessage!, cancellationToken);
                    return res;
                }
            }

            // 3. Timestamp & Clock Skew / Retention Expiration Validation
            DateTime occurredAt = item.Timestamp != default ? item.Timestamp : DateTime.UtcNow;
            var now = DateTime.UtcNow;

            if (occurredAt > now.AddMinutes(_options.MaxFutureClockSkewMinutes))
            {
                _logger.LogWarning("Clock skew violation for event {EventId}: OccurredAt {OccurredAt} is in future (> {MaxSkew}m ahead of server time).",
                    item.EventId, occurredAt, _options.MaxFutureClockSkewMinutes);

                var res = CreateResult(item.EventId, OfflineOrderingStatus.SequenceConflict, OfflineReconciliationStatus.Invalid,
                    OfflineReasonCode.FutureTimestampClockSkew, "Event timestamp violates maximum allowed future clock skew.", isAcceptedForAck: false);
                await TryRecordDeadLetterAsync(item, authPcId, workstation.Id, workstation.SiteId, site?.OrganizationId, batchId, res.ReasonCode ?? "FUTURE_TIMESTAMP_CLOCK_SKEW", res.ErrorMessage!, cancellationToken);
                return res;
            }

            string reliabilityClass = string.IsNullOrWhiteSpace(item.ReliabilityClass) ? EventReliabilityClass.Normal : item.ReliabilityClass.Trim().ToUpperInvariant();
            TimeSpan retentionLimit = GetRetentionLimit(reliabilityClass);

            if (retentionLimit > TimeSpan.Zero && (now - occurredAt) > retentionLimit)
            {
                _logger.LogWarning("Retention expiration for event {EventId} ({Class}): OccurredAt {OccurredAt} exceeds retention threshold {Limit}.",
                    item.EventId, reliabilityClass, occurredAt, retentionLimit);

                var res = CreateResult(item.EventId, OfflineOrderingStatus.Stale, OfflineReconciliationStatus.Expired,
                    OfflineReasonCode.ExpiredRetention, $"Event timestamp exceeds {reliabilityClass} retention limit.", isAcceptedForAck: false);
                await TryRecordDeadLetterAsync(item, authPcId, workstation.Id, workstation.SiteId, site?.OrganizationId, batchId, res.ReasonCode ?? "EXPIRED_RETENTION", res.ErrorMessage!, cancellationToken);
                return res;
            }

            // 4. Session Reference Validation
            string? payloadSessionId = ExtractStringProperty(payloadText, "sessionId");
            if (!string.IsNullOrEmpty(payloadSessionId) && Guid.TryParse(payloadSessionId, out var sessionGuid))
            {
                var session = await _sessionRepository.FirstOrDefaultAsync(s => s.Id == sessionGuid, false, cancellationToken);
                if (session == null)
                {
                    _logger.LogWarning("CONFLICT ALERT: Offline event {EventId} references non-existent session {SessionId}.", item.EventId, payloadSessionId);
                    var res = CreateResult(item.EventId, OfflineOrderingStatus.SequenceConflict, OfflineReconciliationStatus.Conflict,
                        OfflineReasonCode.InvalidSessionReference, "Referenced session does not exist.", isAcceptedForAck: false);
                    await TryRecordDeadLetterAsync(item, authPcId, workstation.Id, workstation.SiteId, site?.OrganizationId, batchId, res.ReasonCode ?? "INVALID_SESSION_REFERENCE", res.ErrorMessage!, cancellationToken);
                    return res;
                }
            }

            // 5. Deduplication & Payload Hash Integrity Check
            var existingEvent = await _processedEventRepository.GetByEventIdAsync(eventGuid, true, cancellationToken);
            if (existingEvent != null)
            {
                existingEvent.LastReceivedAt = now;

                if (existingEvent.PayloadHash.Equals(payloadHash, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("Duplicate offline event {EventId} in batch {BatchId}. Idempotently ACKed.", item.EventId, batchId);
                    existingEvent.ProcessingStatus = OfflineReconciliationStatus.Duplicate;
                    existingEvent.OrderingStatus = OfflineOrderingStatus.Duplicate;
                    existingEvent.ReasonCode = OfflineReasonCode.StaleSequence;

                    try
                    {
                        await _unitOfWork.SaveChangesAsync(cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Concurrency exception during duplicate event update for {EventId}. Returning idempotent ACK.", item.EventId);
                    }

                    return CreateResult(item.EventId, OfflineOrderingStatus.Duplicate, OfflineReconciliationStatus.Duplicate,
                        OfflineReasonCode.StaleSequence, "Duplicate event already processed.", isAcceptedForAck: true, existingEvent);
                }
                else
                {
                    _logger.LogWarning("CONFLICT ALERT: EventId {EventId} retransmitted with modified payload. Original hash={OrigHash}, New hash={NewHash}.",
                        item.EventId, existingEvent.PayloadHash, payloadHash);

                    existingEvent.ProcessingStatus = OfflineReconciliationStatus.Conflict;
                    existingEvent.OrderingStatus = OfflineOrderingStatus.SequenceConflict;
                    existingEvent.ReasonCode = OfflineReasonCode.PayloadHashConflict;
                    existingEvent.ErrorMessage = "Conflicting payload hash for existing EventId.";

                    try
                    {
                        await _unitOfWork.SaveChangesAsync(cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Concurrency exception during conflict event update for {EventId}.", item.EventId);
                    }

                    var res = CreateResult(item.EventId, OfflineOrderingStatus.SequenceConflict, OfflineReconciliationStatus.Conflict,
                        OfflineReasonCode.PayloadHashConflict, "Conflicting payload hash for existing EventId.", isAcceptedForAck: false, existingEvent);
                    await TryRecordDeadLetterAsync(item, authPcId, workstation.Id, workstation.SiteId, site?.OrganizationId, batchId, res.ReasonCode ?? "PAYLOAD_HASH_CONFLICT", res.ErrorMessage!, cancellationToken);
                    return res;
                }
            }

            // 6. Stream-Aware Monotonic Ordering Evaluation
            bool isStrictlyOrdered = reliabilityClass == EventReliabilityClass.Critical || reliabilityClass == EventReliabilityClass.Important;

            string orderingStatus;
            string reconciliationStatus;
            string reasonCode;
            long expectedSequence = 0;
            long receivedSequence = item.SequenceNumber;

            if (!isStrictlyOrdered)
            {
                // Unordered event class (NORMAL, EPHEMERAL, NOT_QUEUEABLE)
                orderingStatus = OfflineOrderingStatus.Unordered;
                reconciliationStatus = OfflineReconciliationStatus.ReadyForReconciliation;
                reasonCode = OfflineReasonCode.SuccessUnordered;
            }
            else
            {
                // Strict stream ordering check for CRITICAL and IMPORTANT events
                var streamState = await _streamStateRepository.GetByClientIdAsync(authPcId, true, cancellationToken);
                if (streamState == null)
                {
                    streamState = new WorkstationStreamState
                    {
                        ClientId = authPcId,
                        WorkstationId = workstation.Id,
                        LastSequenceNumber = 0,
                        LastProcessedAt = now
                    };
                    await _streamStateRepository.AddAsync(streamState, cancellationToken);
                }

                expectedSequence = streamState.LastSequenceNumber + 1;

                if (receivedSequence == expectedSequence || (streamState.LastSequenceNumber == 0 && receivedSequence == 1))
                {
                    // IN_ORDER Event
                    orderingStatus = OfflineOrderingStatus.InOrder;
                    reconciliationStatus = OfflineReconciliationStatus.ReadyForReconciliation;
                    reasonCode = OfflineReasonCode.SuccessInOrder;

                    streamState.LastSequenceNumber = receivedSequence;
                    streamState.LastOccurredAt = occurredAt;
                    streamState.LastProcessedAt = now;
                }
                else if (receivedSequence > expectedSequence)
                {
                    // SEQUENCE GAP DETECTED
                    _logger.LogInformation("Sequence gap detected for workstation {PcId}: Received sequence {Received}, Expected sequence {Expected}.",
                        authPcId, receivedSequence, expectedSequence);

                    if (_options.GapPolicy.Equals("ACCEPT_WITH_GAP", StringComparison.OrdinalIgnoreCase))
                    {
                        orderingStatus = OfflineOrderingStatus.GapDetected;
                        reconciliationStatus = OfflineReconciliationStatus.ReadyForReconciliation;
                        reasonCode = OfflineReasonCode.SuccessGapAccepted;

                        streamState.LastSequenceNumber = receivedSequence;
                        streamState.LastOccurredAt = occurredAt;
                        streamState.LastProcessedAt = now;
                    }
                    else
                    {
                        // Default GAP Policy: WAIT
                        orderingStatus = OfflineOrderingStatus.GapDetected;
                        reconciliationStatus = OfflineReconciliationStatus.WaitingForSequence;
                        reasonCode = OfflineReasonCode.SequenceGapWait;
                    }
                }
                else
                {
                    // STALE / Older sequence number
                    _logger.LogWarning("Stale sequence received for workstation {PcId}: Received sequence {Received} <= Last sequence {Last}.",
                        authPcId, receivedSequence, streamState.LastSequenceNumber);

                    orderingStatus = OfflineOrderingStatus.Stale;
                    reconciliationStatus = OfflineReconciliationStatus.Duplicate;
                    reasonCode = OfflineReasonCode.StaleSequence;
                }
            }

            // 7. Business Reconciliation Integration (Stage 09-07)
            string? businessErrorMessage = null;

            if (reconciliationStatus == OfflineReconciliationStatus.ReadyForReconciliation && _businessReconciliationService != null)
            {
                try
                {
                    var bizRes = await _businessReconciliationService.ReconcileAsync(item, workstation, site, batchId, cancellationToken);
                    reconciliationStatus = bizRes.Status;
                    reasonCode = bizRes.ReasonCode;
                    businessErrorMessage = bizRes.ErrorMessage;
                }
                catch (Exception bizEx)
                {
                    _logger.LogError(bizEx, "Error during business reconciliation for event {EventId}.", item.EventId);
                    reconciliationStatus = OfflineReconciliationStatus.Conflict;
                    reasonCode = "BUSINESS_RECONCILIATION_ERROR";
                    businessErrorMessage = bizEx.Message;
                }
            }

            // 8. Persist ProcessedEvent record & Audit Entry
            var processedEvent = new ProcessedEvent
            {
                EventId = eventGuid,
                BatchId = batchId,
                ClientId = authPcId,
                WorkstationId = workstation.Id,
                EventType = item.EventType ?? string.Empty,
                SequenceNumber = receivedSequence,
                ReliabilityClass = reliabilityClass,
                OrderingStatus = orderingStatus,
                ProcessingStatus = reconciliationStatus,
                PayloadHash = payloadHash,
                ReasonCode = reasonCode,
                ErrorMessage = businessErrorMessage,
                OccurredAt = occurredAt,
                FirstReceivedAt = now,
                LastReceivedAt = now,
                ProcessedAt = now,
                ReconciledAt = (reconciliationStatus == OfflineReconciliationStatus.Accepted || reconciliationStatus == OfflineReconciliationStatus.ReadyForReconciliation) ? now : null
            };

            await _processedEventRepository.AddAsync(processedEvent, cancellationToken);

            // Audit Record Creation
            var auditEvent = new AuditEvent
            {
                EventId = eventGuid,
                EventType = $"OFFLINE_EVENT_{orderingStatus}",
                WorkstationId = workstation.Id,
                CorrelationId = batchId,
                Priority = isStrictlyOrdered ? 1 : 2,
                Timestamp = now,
                Payload = JsonSerializer.Serialize(new
                {
                    eventId = item.EventId,
                    eventType = item.EventType,
                    clientId = authPcId,
                    sequenceNumber = receivedSequence,
                    expectedSequence = expectedSequence,
                    orderingStatus = orderingStatus,
                    reconciliationStatus = reconciliationStatus,
                    reasonCode = reasonCode,
                    errorMessage = businessErrorMessage
                })
            };

            await _auditEventRepository.AddAsync(auditEvent, cancellationToken);

            try
            {
                // Save Db context changes transactionally
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DbUpdateException or ConcurrencyException hit for EventId {EventId} during SaveChangesAsync.", item.EventId);

                for (int retry = 0; retry < 3; retry++)
                {
                    var duplicateEvt = await _processedEventRepository.GetByEventIdAsync(eventGuid, false, cancellationToken);
                    if (duplicateEvt != null)
                    {
                        return CreateResult(item.EventId, OfflineOrderingStatus.Duplicate, OfflineReconciliationStatus.Duplicate,
                            OfflineReasonCode.StaleSequence, "Duplicate event processed concurrently.", isAcceptedForAck: true, duplicateEvt);
                    }
                    await Task.Delay(10, cancellationToken);
                }

                return CreateResult(item.EventId, OfflineOrderingStatus.Duplicate, OfflineReconciliationStatus.Duplicate,
                    OfflineReasonCode.StaleSequence, "Duplicate event processed concurrently.", isAcceptedForAck: true);
            }

            // 9. Gap Resolution Check: If this event was IN_ORDER, unblock any waiting events in the sequence pipeline!
            if (isStrictlyOrdered && orderingStatus == OfflineOrderingStatus.InOrder)
            {
                await ResolveWaitingGapEventsAsync(authPcId, workstation.Id, cancellationToken);
            }

            bool isAcceptedForAck = reconciliationStatus == OfflineReconciliationStatus.ReadyForReconciliation ||
                                   reconciliationStatus == OfflineReconciliationStatus.Accepted ||
                                   reconciliationStatus == OfflineReconciliationStatus.WaitingForSequence ||
                                   reconciliationStatus == OfflineReconciliationStatus.Duplicate;

            if (!isAcceptedForAck)
            {
                await TryRecordDeadLetterAsync(item, authPcId, workstation.Id, workstation.SiteId, site?.OrganizationId, batchId, reasonCode, businessErrorMessage ?? "Event reconciliation failed", cancellationToken);
            }

            return CreateResult(item.EventId, orderingStatus, reconciliationStatus, reasonCode, businessErrorMessage, isAcceptedForAck, processedEvent, expectedSequence, receivedSequence);
        }

        private async Task TryRecordDeadLetterAsync(
            OfflineQueueItem item,
            string clientId,
            Guid? workstationId,
            string? siteId,
            Guid? organizationId,
            string batchId,
            string failureCode,
            string failureReason,
            CancellationToken cancellationToken)
        {
            if (_deadLetterRepository == null) return;
            if (!Guid.TryParse(item.EventId, out var eventGuid)) return;

            try
            {
                var existingDlq = await _deadLetterRepository.GetByEventIdAsync(eventGuid, cancellationToken);
                if (existingDlq == null)
                {
                    string payloadText = item.Payload is JsonElement elem ? elem.GetRawText() : (item.Payload?.ToString() ?? "{}");
                    var dlq = new DeadLetterEvent
                    {
                        EventId = eventGuid,
                        BatchId = batchId,
                        ClientId = clientId,
                        WorkstationId = workstationId,
                        SiteId = siteId,
                        OrganizationId = organizationId,
                        EventType = item.EventType ?? "UNKNOWN",
                        SequenceNumber = item.SequenceNumber,
                        ReliabilityClass = item.ReliabilityClass ?? "NORMAL",
                        Payload = payloadText,
                        FailureCode = failureCode,
                        FailureReason = failureReason,
                        RetryCount = 1,
                        FirstSeenAt = DateTime.UtcNow,
                        LastAttemptAt = DateTime.UtcNow,
                        DeadLetteredAt = DateTime.UtcNow,
                        CorrelationId = batchId,
                        ProcessingStatus = failureCode == OfflineReasonCode.ExpiredRetention ? DeadLetterStatus.Expired : DeadLetterStatus.DeadLetter
                    };

                    await _deadLetterRepository.AddAsync(dlq, cancellationToken);

                    var auditEvent = new AuditEvent
                    {
                        EventId = Guid.NewGuid(),
                        EventType = "OFFLINE_EVENT_DLQ",
                        WorkstationId = workstationId,
                        CorrelationId = batchId,
                        Priority = 1,
                        Timestamp = DateTime.UtcNow,
                        Payload = JsonSerializer.Serialize(new
                        {
                            eventId = item.EventId,
                            eventType = item.EventType,
                            clientId = clientId,
                            failureCode = failureCode,
                            failureReason = failureReason,
                            deadLetteredAt = dlq.DeadLetteredAt
                        })
                    };

                    await _auditEventRepository.AddAsync(auditEvent, cancellationToken);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist DeadLetterEvent for EventId {EventId}.", item.EventId);
            }
        }

        private async Task ResolveWaitingGapEventsAsync(string clientId, Guid workstationId, CancellationToken cancellationToken)
        {
            var streamState = await _streamStateRepository.GetByClientIdAsync(clientId, true, cancellationToken);
            if (streamState == null) return;

            bool updatedAny = false;
            while (true)
            {
                long nextExpected = streamState.LastSequenceNumber + 1;
                var waitingEvents = await _streamStateRepository.GetPendingWaitingEventsAsync(clientId, nextExpected, cancellationToken);

                if (waitingEvents == null || waitingEvents.Count == 0)
                {
                    break;
                }

                foreach (var waitingEvent in waitingEvents)
                {
                    _logger.LogInformation("Unblocking sequence gap event {EventId} (Seq {Seq}) for workstation {PcId}.",
                        waitingEvent.EventId, waitingEvent.SequenceNumber, clientId);

                    waitingEvent.OrderingStatus = OfflineOrderingStatus.InOrder;
                    waitingEvent.ProcessingStatus = OfflineReconciliationStatus.ReadyForReconciliation;
                    waitingEvent.ReasonCode = OfflineReasonCode.SuccessInOrder;
                    waitingEvent.ReconciledAt = DateTime.UtcNow;

                    streamState.LastSequenceNumber = waitingEvent.SequenceNumber;
                    streamState.LastProcessedAt = DateTime.UtcNow;
                    updatedAny = true;
                }
            }

            if (updatedAny)
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
        }

        private static TimeSpan GetRetentionLimit(string reliabilityClass)
        {
            return reliabilityClass switch
            {
                EventReliabilityClass.Critical => TimeSpan.FromDays(30),
                EventReliabilityClass.Important => TimeSpan.FromDays(7),
                EventReliabilityClass.Normal => TimeSpan.FromDays(1),
                _ => TimeSpan.FromDays(1)
            };
        }

        private static OrderingAndReconciliationResult CreateResult(
            string eventId,
            string orderingStatus,
            string reconciliationStatus,
            string reasonCode,
            string? errorMessage,
            bool isAcceptedForAck,
            ProcessedEvent? entity = null,
            long expectedSeq = 0,
            long receivedSeq = 0)
        {
            return new OrderingAndReconciliationResult
            {
                EventId = eventId,
                OrderingStatus = orderingStatus,
                ReconciliationStatus = reconciliationStatus,
                ReasonCode = reasonCode,
                ErrorMessage = errorMessage,
                IsAcceptedForAck = isAcceptedForAck,
                ProcessedEventEntity = entity,
                ExpectedSequence = expectedSeq,
                ReceivedSequence = receivedSeq
            };
        }

        private static string ComputeSha256(byte[] data)
        {
            using var sha256 = SHA256.Create();
            byte[] hashBytes = sha256.ComputeHash(data);
            var sb = new StringBuilder(hashBytes.Length * 2);
            foreach (byte b in hashBytes)
            {
                sb.Append(b.ToString("x2"));
            }
            return sb.ToString();
        }

        private static string? ExtractStringProperty(string json, string propertyName)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        if (prop.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                        {
                            return prop.Value.GetString();
                        }
                    }
                }
            }
            catch
            {
                // Fallback for non-JSON or invalid strings
            }
            return null;
        }
    }
}
