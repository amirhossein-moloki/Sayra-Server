using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Contracts;
using Sayra.Backend.Shared;

namespace Sayra.Backend.Application.OfflineQueue
{
    public class IngestOfflineBatchCommandHandler : ICommandHandler<IngestOfflineBatchCommand, IngestOfflineBatchResult>
    {
        private readonly ILogger<IngestOfflineBatchCommandHandler> _logger;
        private const int MaxBatchItemCount = 100;
        private const int MaxSinglePayloadBytes = 256 * 1024; // 256 KB

        public IngestOfflineBatchCommandHandler(ILogger<IngestOfflineBatchCommandHandler> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task<Result<IngestOfflineBatchResult>> HandleAsync(IngestOfflineBatchCommand command, CancellationToken cancellationToken = default)
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
                return Task.FromResult(Result<IngestOfflineBatchResult>.Success(new IngestOfflineBatchResult(ack)));
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
                return Task.FromResult(Result<IngestOfflineBatchResult>.Success(new IngestOfflineBatchResult(ack)));
            }

            // Empty Batch Check
            if (request.Items == null || request.Items.Count == 0)
            {
                _logger.LogInformation("Received empty offline sync batch {BatchId} from {PcId}.", request.BatchId, authPcId);
                ack.Success = true;
                ack.ProcessedCount = 0;
                return Task.FromResult(Result<IngestOfflineBatchResult>.Success(new IngestOfflineBatchResult(ack)));
            }

            // Batch Size Bounds Check
            if (request.Items.Count > MaxBatchItemCount)
            {
                _logger.LogWarning("Offline batch {BatchId} exceeds maximum items limit ({Count} > {Limit}).", request.BatchId, request.Items.Count, MaxBatchItemCount);
                ack.ErrorMessage = $"Batch size exceeds maximum limit of {MaxBatchItemCount} items.";
                return Task.FromResult(Result<IngestOfflineBatchResult>.Success(new IngestOfflineBatchResult(ack)));
            }

            var acceptedIds = new List<string>();
            var rejectedIds = new List<string>();

            foreach (var item in request.Items)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.EventId) || !Guid.TryParse(item.EventId, out _))
                {
                    _logger.LogWarning("Rejecting item in batch {BatchId}: Invalid or missing EventId.", request.BatchId);
                    if (item != null && !string.IsNullOrWhiteSpace(item.EventId))
                    {
                        rejectedIds.Add(item.EventId);
                    }
                    continue;
                }

                // Check payload size
                string payloadText = item.Payload is JsonElement elem ? elem.GetRawText() : (item.Payload?.ToString() ?? "{}");
                if (System.Text.Encoding.UTF8.GetByteCount(payloadText) > MaxSinglePayloadBytes)
                {
                    _logger.LogWarning("Rejecting event {EventId} in batch {BatchId}: Payload exceeds 256 KB limit.", item.EventId, request.BatchId);
                    rejectedIds.Add(item.EventId);
                    continue;
                }

                // Stage 09-03 transport ingestion validation succeeded
                acceptedIds.Add(item.EventId);
            }

            ack.AcknowledgedEventIds = acceptedIds;
            ack.RejectedEventIds = rejectedIds;
            ack.ProcessedCount = acceptedIds.Count;
            ack.Success = true;

            _logger.LogInformation("Successfully ingested offline batch {BatchId} from {PcId}: Accepted={AcceptedCount}, Rejected={RejectedCount}.",
                request.BatchId, authPcId, acceptedIds.Count, rejectedIds.Count);

            return Task.FromResult(Result<IngestOfflineBatchResult>.Success(new IngestOfflineBatchResult(ack)));
        }
    }
}
