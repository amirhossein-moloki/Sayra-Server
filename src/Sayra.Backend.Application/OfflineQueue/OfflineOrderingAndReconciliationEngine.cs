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
        private readonly ILogger<OfflineOrderingAndReconciliationEngine> _logger;

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
            IRedisService? redisService = null)
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
        }

        public async Task<OrderingAndReconciliationResult> EvaluateAndReconcileAsync(
            OfflineQueueItem item,
            string authenticatedPcId,
            string batchId,
            CancellationToken cancellationToken = default)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            string authPcId = authenticatedPcId?.Trim().ToUpperInvariant() ?? string.Empty;

            // 1. Basic Envelope & Identity Mismatch Check
            if (string.IsNullOrEmpty(item.EventId) || !Guid.TryParse(item.EventId, out var eventGuid))
            {
                _logger.LogWarning("Rejecting offline event in batch {BatchId}: Invalid or missing EventId '{EventId}'.", batchId, item.EventId);
                return CreateResult(item.EventId ?? string.Empty, OfflineOrderingStatus.Unordered, OfflineReconciliationStatus.Rejected,
                    OfflineReasonCode.MalformedPayload, "Invalid or missing EventId.", isAcceptedForAck: false);
            }

            // Extract payload & payload hash
            string payloadText = item.Payload is JsonElement elem ? elem.GetRawText() : (item.Payload?.ToString() ?? "{}");
            byte[] payloadBytes = Encoding.UTF8.GetBytes(payloadText);

            if (payloadBytes.Length > MaxSinglePayloadBytes)
            {
                _logger.LogWarning("Rejecting offline event {EventId} in batch {BatchId}: Payload exceeds 256 KB limit.", item.EventId, batchId);
                return CreateResult(item.EventId, OfflineOrderingStatus.Unordered, OfflineReconciliationStatus.Rejected,
                    OfflineReasonCode.MalformedPayload, "Payload exceeds 256 KB limit.", isAcceptedForAck: false);
            }

            string payloadHash = ComputeSha256(payloadBytes);

            // Check if payload contains ClientId / WorkstationId that conflicts with authenticated identity
            string? payloadClientId = ExtractStringProperty(payloadText, "clientId") ?? ExtractStringProperty(payloadText, "workstationId");
            if (!string.IsNullOrEmpty(payloadClientId) && !payloadClientId.Trim().Equals(authPcId, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("SECURITY ALERT: Offline event {EventId} identity mismatch. Connection PcId={AuthPcId}, Payload identity={PayloadClientId}.",
                    item.EventId, authPcId, payloadClientId);

                return CreateResult(item.EventId, OfflineOrderingStatus.SequenceConflict, OfflineReconciliationStatus.Rejected,
                    OfflineReasonCode.IdentityMismatch, "Payload client identity does not match authenticated TCP session identity.", isAcceptedForAck: false);
            }

            // 2. Authoritative Workstation & Site Lookup
            var workstation = await _workstationRepository.FirstOrDefaultAsync(w => w.PcId == authPcId, false, cancellationToken);
            if (workstation == null)
            {
                _logger.LogWarning("CONFLICT ALERT: Offline event {EventId} references non-existent workstation PcId={AuthPcId}.", item.EventId, authPcId);
                return CreateResult(item.EventId, OfflineOrderingStatus.SequenceConflict, OfflineReconciliationStatus.Conflict,
                    OfflineReasonCode.WorkstationNotFound, "Workstation does not exist on authoritative server.", isAcceptedForAck: false);
            }

            if (!string.IsNullOrWhiteSpace(workstation.SiteId))
            {
                var site = await _siteRepository.FirstOrDefaultAsync(s => s.SiteId == workstation.SiteId || s.Code == workstation.SiteId, false, cancellationToken);
                if (site != null && !site.CanOperate())
                {
                    _logger.LogWarning("CONFLICT ALERT: Offline event {EventId} references inactive site {SiteId} for workstation PcId={AuthPcId}.",
                        item.EventId, workstation.SiteId, authPcId);
                    return CreateResult(item.EventId, OfflineOrderingStatus.SequenceConflict, OfflineReconciliationStatus.Conflict,
                        OfflineReasonCode.SiteMismatch, "Workstation site is inactive.", isAcceptedForAck: false);
                }
            }

            // 3. Timestamp & Clock Skew / Retention Expiration Validation
            DateTime occurredAt = item.Timestamp != default ? item.Timestamp : DateTime.UtcNow;
            var now = DateTime.UtcNow;

            if (occurredAt > now.AddMinutes(_options.MaxFutureClockSkewMinutes))
            {
                _logger.LogWarning("Clock skew violation for event {EventId}: OccurredAt {OccurredAt} is in future (> {MaxSkew}m ahead of server time).",
                    item.EventId, occurredAt, _options.MaxFutureClockSkewMinutes);

                return CreateResult(item.EventId, OfflineOrderingStatus.SequenceConflict, OfflineReconciliationStatus.Invalid,
                    OfflineReasonCode.FutureTimestampClockSkew, "Event timestamp violates maximum allowed future clock skew.", isAcceptedForAck: false);
            }

            string reliabilityClass = string.IsNullOrWhiteSpace(item.ReliabilityClass) ? EventReliabilityClass.Normal : item.ReliabilityClass.Trim().ToUpperInvariant();
            TimeSpan retentionLimit = GetRetentionLimit(reliabilityClass);

            if (retentionLimit > TimeSpan.Zero && (now - occurredAt) > retentionLimit)
            {
                _logger.LogWarning("Retention expiration for event {EventId} ({Class}): OccurredAt {OccurredAt} exceeds retention threshold {Limit}.",
                    item.EventId, reliabilityClass, occurredAt, retentionLimit);

                return CreateResult(item.EventId, OfflineOrderingStatus.Stale, OfflineReconciliationStatus.Expired,
                    OfflineReasonCode.ExpiredRetention, $"Event timestamp exceeds {reliabilityClass} retention limit.", isAcceptedForAck: false);
            }

            // 4. Session Reference Validation
            string? payloadSessionId = ExtractStringProperty(payloadText, "sessionId");
            if (!string.IsNullOrEmpty(payloadSessionId) && Guid.TryParse(payloadSessionId, out var sessionGuid))
            {
                var session = await _sessionRepository.FirstOrDefaultAsync(s => s.Id == sessionGuid, false, cancellationToken);
                if (session == null)
                {
                    _logger.LogWarning("CONFLICT ALERT: Offline event {EventId} references non-existent session {SessionId}.", item.EventId, payloadSessionId);
                    return CreateResult(item.EventId, OfflineOrderingStatus.SequenceConflict, OfflineReconciliationStatus.Conflict,
                        OfflineReasonCode.InvalidSessionReference, "Referenced session does not exist.", isAcceptedForAck: false);
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
                    await _unitOfWork.SaveChangesAsync(cancellationToken);

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
                    await _unitOfWork.SaveChangesAsync(cancellationToken);

                    return CreateResult(item.EventId, OfflineOrderingStatus.SequenceConflict, OfflineReconciliationStatus.Conflict,
                        OfflineReasonCode.PayloadHashConflict, "Conflicting payload hash for existing EventId.", isAcceptedForAck: false, existingEvent);
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

            // 7. Persist ProcessedEvent record & Audit Entry
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
                OccurredAt = occurredAt,
                FirstReceivedAt = now,
                LastReceivedAt = now,
                ProcessedAt = now,
                ReconciledAt = reconciliationStatus == OfflineReconciliationStatus.ReadyForReconciliation ? now : null
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
                    reasonCode = reasonCode
                })
            };

            await _auditEventRepository.AddAsync(auditEvent, cancellationToken);

            // Save Db context changes transactionally
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // 8. Gap Resolution Check: If this event was IN_ORDER, unblock any waiting events in the sequence pipeline!
            if (isStrictlyOrdered && orderingStatus == OfflineOrderingStatus.InOrder)
            {
                await ResolveWaitingGapEventsAsync(authPcId, workstation.Id, cancellationToken);
            }

            bool isAcceptedForAck = reconciliationStatus == OfflineReconciliationStatus.ReadyForReconciliation ||
                                   reconciliationStatus == OfflineReconciliationStatus.Accepted ||
                                   reconciliationStatus == OfflineReconciliationStatus.WaitingForSequence ||
                                   reconciliationStatus == OfflineReconciliationStatus.Duplicate;

            return CreateResult(item.EventId, orderingStatus, reconciliationStatus, reasonCode, null, isAcceptedForAck, processedEvent, expectedSequence, receivedSequence);
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
