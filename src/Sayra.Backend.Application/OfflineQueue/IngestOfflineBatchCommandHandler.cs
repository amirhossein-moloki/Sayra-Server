using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Sayra.Backend.Application.Abstractions.Caching;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Shared;

namespace Sayra.Backend.Application.OfflineQueue
{
    public class IngestOfflineBatchCommandHandler : ICommandHandler<IngestOfflineBatchCommand, IngestOfflineBatchResult>
    {
        private readonly IProcessedEventRepository _processedEventRepository;
        private readonly IRepository<Workstation> _workstationRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IRedisService? _redisService;
        private readonly ILogger<IngestOfflineBatchCommandHandler> _logger;
        private const int MaxBatchItemCount = 100;
        private const int MaxSinglePayloadBytes = 256 * 1024; // 256 KB

        public IngestOfflineBatchCommandHandler(
            IProcessedEventRepository processedEventRepository,
            IRepository<Workstation> workstationRepository,
            IUnitOfWork unitOfWork,
            ILogger<IngestOfflineBatchCommandHandler> logger,
            IRedisService? redisService = null)
        {
            _processedEventRepository = processedEventRepository ?? throw new ArgumentNullException(nameof(processedEventRepository));
            _workstationRepository = workstationRepository ?? throw new ArgumentNullException(nameof(workstationRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _redisService = redisService;
        }

        public async Task<Result<IngestOfflineBatchResult>> HandleAsync(IngestOfflineBatchCommand command, CancellationToken cancellationToken = default)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));

            var request = command.BatchRequest;
            var ack = new OfflineBatchAcknowledgment
            {
                BatchId = request?.BatchId ?? string.Empty,
                Success = false,
                ProcessedCount = 0
            };

            if (request == null)
            {
                ack.ErrorMessage = "Batch request cannot be null.";
                return Result<IngestOfflineBatchResult>.Success(new IngestOfflineBatchResult(ack));
            }

            // Identity Binding Check
            string authPcId = command.AuthenticatedPcId?.Trim().ToUpperInvariant() ?? string.Empty;
            string clientPcId = request.ClientId?.Trim().ToUpperInvariant() ?? string.Empty;
            string workstationPcId = request.WorkstationId?.Trim().ToUpperInvariant() ?? string.Empty;

            if (string.IsNullOrEmpty(authPcId) || (!authPcId.Equals(clientPcId, StringComparison.OrdinalIgnoreCase) && !authPcId.Equals(workstationPcId, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogWarning("SECURITY ALERT: Offline batch identity mismatch. Connection PcId={AuthPcId}, Payload ClientId={ClientPcId}, WorkstationId={WorkstationPcId}.",
                    authPcId, clientPcId, workstationPcId);

                ack.ErrorMessage = "SECURITY_VIOLATION: Payload client identity does not match authenticated TCP session identity.";
                return Result<IngestOfflineBatchResult>.Success(new IngestOfflineBatchResult(ack));
            }

            // Resolve Authoritative Workstation Entity
            var workstation = await _workstationRepository.FirstOrDefaultAsync(w => w.PcId == authPcId, false, cancellationToken);
            Guid? resolvedWorkstationId = workstation?.Id;

            // Empty Batch Check
            if (request.Items == null || request.Items.Count == 0)
            {
                _logger.LogInformation("Received empty offline sync batch {BatchId} from {PcId}.", request.BatchId, authPcId);
                ack.Success = true;
                ack.ProcessedCount = 0;
                return Result<IngestOfflineBatchResult>.Success(new IngestOfflineBatchResult(ack));
            }

            // Batch Size Bounds Check
            if (request.Items.Count > MaxBatchItemCount)
            {
                _logger.LogWarning("Offline batch {BatchId} exceeds maximum items limit ({Count} > {Limit}).", request.BatchId, request.Items.Count, MaxBatchItemCount);
                ack.ErrorMessage = $"Batch size exceeds maximum limit of {MaxBatchItemCount} items.";
                return Result<IngestOfflineBatchResult>.Success(new IngestOfflineBatchResult(ack));
            }

            var acceptedIds = new List<string>();
            var rejectedIds = new List<string>();

            foreach (var item in request.Items)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.EventId) || !Guid.TryParse(item.EventId, out var eventGuid))
                {
                    _logger.LogWarning("Rejecting item in batch {BatchId}: Invalid or missing EventId.", request.BatchId);
                    if (item != null && !string.IsNullOrWhiteSpace(item.EventId))
                    {
                        rejectedIds.Add(item.EventId);
                    }
                    continue;
                }

                // Payload extraction & size check
                string payloadText = item.Payload is JsonElement elem ? elem.GetRawText() : (item.Payload?.ToString() ?? "{}");
                byte[] payloadBytes = Encoding.UTF8.GetBytes(payloadText);
                if (payloadBytes.Length > MaxSinglePayloadBytes)
                {
                    _logger.LogWarning("Rejecting event {EventId} in batch {BatchId}: Payload exceeds 256 KB limit.", item.EventId, request.BatchId);
                    rejectedIds.Add(item.EventId);
                    continue;
                }

                string payloadHash = ComputeSha256(payloadBytes);
                string redisDedupKey = $"v1:event:dedup:{item.EventId}";

                // 1. Fast Redis Idempotency Check
                bool isCachedInRedis = false;
                if (_redisService != null)
                {
                    try
                    {
                        var redisValue = await _redisService.GetAsync<string>(redisDedupKey, cancellationToken);
                        if (!string.IsNullOrEmpty(redisValue))
                        {
                            isCachedInRedis = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Redis lookup failed for event {EventId}, falling back to DB.", item.EventId);
                    }
                }

                // 2. Database Idempotency & Conflict Check
                var existingEvent = await _processedEventRepository.GetByEventIdAsync(eventGuid, true, cancellationToken);

                if (existingEvent != null)
                {
                    existingEvent.LastReceivedAt = DateTime.UtcNow;

                    if (existingEvent.PayloadHash.Equals(payloadHash, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("Duplicate offline event {EventId} detected in batch {BatchId}. Idempotently ACKed.", item.EventId, request.BatchId);
                        existingEvent.ProcessingStatus = "DUPLICATE";
                        acceptedIds.Add(item.EventId);
                    }
                    else
                    {
                        _logger.LogWarning("CONFLICT ALERT: EventId {EventId} retransmitted with modified payload. Original hash={OrigHash}, New hash={NewHash}.",
                            item.EventId, existingEvent.PayloadHash, payloadHash);
                        existingEvent.ProcessingStatus = "CONFLICT";
                        existingEvent.ErrorMessage = "Conflicting payload hash for existing EventId.";
                        rejectedIds.Add(item.EventId);
                    }

                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    continue;
                }

                if (isCachedInRedis)
                {
                    // Redis indicated previously processed, but missing in current DB query
                    _logger.LogInformation("Duplicate offline event {EventId} detected via Redis cache. Idempotently ACKed.", item.EventId);
                    acceptedIds.Add(item.EventId);
                    continue;
                }

                // 3. First Delivery - Persist ProcessedEvent
                var newProcessedEvent = new ProcessedEvent
                {
                    EventId = eventGuid,
                    BatchId = request.BatchId,
                    ClientId = authPcId,
                    WorkstationId = resolvedWorkstationId,
                    EventType = item.EventType ?? string.Empty,
                    SequenceNumber = item.SequenceNumber,
                    ReliabilityClass = item.ReliabilityClass ?? "NORMAL",
                    ProcessingStatus = "ACCEPTED",
                    PayloadHash = payloadHash,
                    FirstReceivedAt = DateTime.UtcNow,
                    LastReceivedAt = DateTime.UtcNow,
                    ProcessedAt = DateTime.UtcNow
                };

                await _processedEventRepository.AddAsync(newProcessedEvent, cancellationToken);

                try
                {
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    acceptedIds.Add(item.EventId);

                    // Set Redis dedup cache (24h TTL)
                    if (_redisService != null)
                    {
                        try
                        {
                            await _redisService.SetAsync(redisDedupKey, "ACCEPTED", TimeSpan.FromHours(24), cancellationToken);
                        }
                        catch (Exception redisEx)
                        {
                            _logger.LogWarning(redisEx, "Failed to cache event {EventId} in Redis.", item.EventId);
                        }
                    }
                }
                catch (Exception dbEx)
                {
                    // Race condition or DB update failure: concurrent duplicate request inserted same EventId
                    _logger.LogWarning(dbEx, "Database persistence error / unique constraint race condition on EventId {EventId}. Handling safely.", item.EventId);

                    var racedEvent = await _processedEventRepository.GetByEventIdAsync(eventGuid, false, cancellationToken);
                    if (racedEvent != null && racedEvent.PayloadHash.Equals(payloadHash, StringComparison.OrdinalIgnoreCase))
                    {
                        acceptedIds.Add(item.EventId);
                    }
                    else
                    {
                        rejectedIds.Add(item.EventId);
                    }
                }
            }

            ack.AcknowledgedEventIds = acceptedIds;
            ack.RejectedEventIds = rejectedIds;
            ack.ProcessedCount = acceptedIds.Count;
            ack.Success = true;

            _logger.LogInformation("Successfully ingested offline batch {BatchId} from {PcId}: Accepted={AcceptedCount}, Rejected={RejectedCount}.",
                request.BatchId, authPcId, acceptedIds.Count, rejectedIds.Count);

            return Result<IngestOfflineBatchResult>.Success(new IngestOfflineBatchResult(ack));
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
    }
}
