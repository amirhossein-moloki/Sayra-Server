using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Sayra.Backend.Application.Abstractions.Caching;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Contracts;
using Sayra.Backend.Shared;

namespace Sayra.Backend.Application.OfflineQueue
{
    public class IngestOfflineBatchCommandHandler : ICommandHandler<IngestOfflineBatchCommand, IngestOfflineBatchResult>
    {
        private readonly IOfflineOrderingAndReconciliationEngine _orderingEngine;
        private readonly ILogger<IngestOfflineBatchCommandHandler> _logger;
        private readonly IRedisService? _redisService;
        private readonly IOfflineMetrics? _metrics;
        private const int MaxBatchItemCount = 100;

        public IngestOfflineBatchCommandHandler(
            IOfflineOrderingAndReconciliationEngine orderingEngine,
            ILogger<IngestOfflineBatchCommandHandler> logger,
            IRedisService? redisService = null,
            IOfflineMetrics? metrics = null)
        {
            _orderingEngine = orderingEngine ?? throw new ArgumentNullException(nameof(orderingEngine));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _redisService = redisService;
            _metrics = metrics;
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

                _metrics?.RecordSecurityRejection("IDENTITY_MISMATCH");
                ack.ErrorMessage = "SECURITY_VIOLATION: Payload client identity does not match authenticated TCP session identity.";
                return Result<IngestOfflineBatchResult>.Success(new IngestOfflineBatchResult(ack));
            }

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
                _metrics?.RecordSecurityRejection("OVERSIZED_BATCH");
                ack.ErrorMessage = $"Batch size exceeds maximum limit of {MaxBatchItemCount} items.";
                return Result<IngestOfflineBatchResult>.Success(new IngestOfflineBatchResult(ack));
            }

            var acceptedIds = new List<string>();
            var rejectedIds = new List<string>();

            foreach (var item in request.Items)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.EventId))
                {
                    _logger.LogWarning("Rejecting item in batch {BatchId}: Invalid or missing EventId.", request.BatchId);
                    if (item != null && !string.IsNullOrWhiteSpace(item.EventId))
                    {
                        rejectedIds.Add(item.EventId);
                    }
                    continue;
                }

                try
                {
                    var evalResult = await _orderingEngine.EvaluateAndReconcileAsync(item, authPcId, request.BatchId, cancellationToken);

                    if (evalResult.IsAcceptedForAck)
                    {
                        acceptedIds.Add(item.EventId);
                        if (evalResult.ReconciliationStatus == OfflineReconciliationStatus.Duplicate)
                        {
                            _metrics?.RecordSyncEventDuplicated(item.EventType);
                        }
                        else
                        {
                            _metrics?.RecordSyncEventAccepted(item.EventType, item.ReliabilityClass);
                        }

                        // Transient Redis Deduplication Cache (24h TTL)
                        if (_redisService != null)
                        {
                            try
                            {
                                string redisDedupKey = $"v1:event:dedup:{item.EventId}";
                                await _redisService.SetAsync(redisDedupKey, evalResult.ReconciliationStatus, TimeSpan.FromHours(24), cancellationToken);
                            }
                            catch (Exception redisEx)
                            {
                                _metrics?.RecordInfrastructureFailure("REDIS", "DedupCache");
                                _logger.LogWarning(redisEx, "Failed to cache event {EventId} in Redis.", item.EventId);
                            }
                        }
                    }
                    else
                    {
                        rejectedIds.Add(item.EventId);
                        if (evalResult.ReconciliationStatus == OfflineReconciliationStatus.Conflict)
                        {
                            _metrics?.RecordSyncEventConflicted(item.EventType, evalResult.ReasonCode ?? "CONFLICT");
                        }
                        else if (evalResult.ReconciliationStatus == OfflineReconciliationStatus.WaitingForSequence)
                        {
                            _metrics?.RecordSyncEventDeferred(item.EventType);
                        }
                        else
                        {
                            _metrics?.RecordSyncEventRejected(item.EventType, evalResult.ReasonCode ?? "REJECTED");
                        }
                        ack.ErrorMessage = evalResult.ErrorMessage ?? evalResult.ReasonCode ?? "Item rejected";
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing event {EventId} in batch {BatchId}.", item.EventId, request.BatchId);
                    rejectedIds.Add(item.EventId);
                    ack.ErrorMessage = ex.Message;
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
    }
}
