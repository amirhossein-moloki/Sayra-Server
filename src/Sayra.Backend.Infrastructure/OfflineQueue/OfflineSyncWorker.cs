using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sayra.Backend.Application.OfflineQueue;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Infrastructure.OfflineQueue
{
    public class OfflineSyncWorker : IOfflineSyncWorker
    {
        private readonly IDurableOfflineQueue _queue;
        private readonly OfflineSyncWorkerOptions _options;
        private readonly ILogger<OfflineSyncWorker> _logger;
        private readonly SemaphoreSlim _workerLock = new(1, 1);

        public bool IsRunning => _workerLock.CurrentCount == 0;

        public OfflineSyncWorker(
            IDurableOfflineQueue queue,
            IOptions<OfflineSyncWorkerOptions> options,
            ILogger<OfflineSyncWorker> logger)
        {
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<OfflineBatchAcknowledgment?> SynchronizeBatchAsync(
            string clientId,
            string workstationId,
            Func<OfflineBatchRequest, CancellationToken, Task<OfflineBatchAcknowledgment?>> sendBatchFunc,
            CancellationToken cancellationToken = default)
        {
            if (sendBatchFunc == null) throw new ArgumentNullException(nameof(sendBatchFunc));

            if (!await _workerLock.WaitAsync(0, cancellationToken))
            {
                _logger.LogInformation("Synchronization already in progress for client {ClientId}. Skipping duplicate trigger.", clientId);
                return null;
            }

            try
            {
                _logger.LogDebug("Starting sync batch claim for client {ClientId}, workstation {WorkstationId}...", clientId, workstationId);

                var items = await _queue.ClaimBatchAsync(_options.MaxBatchSize, cancellationToken);
                if (items.Count == 0)
                {
                    _logger.LogDebug("No eligible offline items to synchronize for client {ClientId}.", clientId);
                    return new OfflineBatchAcknowledgment
                    {
                        BatchId = Guid.NewGuid().ToString("N"),
                        ProcessedCount = 0,
                        Success = true
                    };
                }

                string batchId = Guid.NewGuid().ToString("N");
                var batchRequest = new OfflineBatchRequest
                {
                    BatchId = batchId,
                    ClientId = clientId,
                    WorkstationId = workstationId,
                    ContractVersion = "1.0",
                    Items = items.Select(MapToContractItem).ToList()
                };

                _logger.LogInformation("Constructed offline sync batch {BatchId} with {Count} items for client {ClientId}.",
                    batchId, batchRequest.Items.Count, clientId);

                OfflineBatchAcknowledgment? ack;
                try
                {
                    ack = await sendBatchFunc(batchRequest, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Transport failure during offline batch {BatchId} submission. Preserving items in queue for retry.", batchId);
                    return null;
                }

                if (ack == null)
                {
                    _logger.LogWarning("Received null ACK for batch {BatchId}. Leaving items in queue for retry.", batchId);
                    return null;
                }

                _logger.LogInformation("Processing ACK for batch {BatchId}: Success={Success}, Acknowledged={AckCount}, Rejected={RejCount}",
                    batchId, ack.Success, ack.AcknowledgedEventIds.Count, ack.RejectedEventIds.Count);

                if (ack.AcknowledgedEventIds.Count > 0)
                {
                    await _queue.AcknowledgeItemsAsync(ack.AcknowledgedEventIds, cancellationToken);
                }

                if (ack.RejectedEventIds.Count > 0)
                {
                    foreach (var rejectedId in ack.RejectedEventIds)
                    {
                        await _queue.MarkFailedAsync(rejectedId, ack.ErrorMessage ?? "Permanently rejected by backend", isPermanent: true, cancellationToken);
                    }
                }

                return ack;
            }
            finally
            {
                _workerLock.Release();
            }
        }

        private static OfflineQueueItem MapToContractItem(DurableQueueItemEntity entity)
        {
            object payloadObj;
            try
            {
                using var doc = JsonDocument.Parse(entity.Payload ?? "{}");
                payloadObj = doc.RootElement.Clone();
            }
            catch
            {
                payloadObj = entity.Payload ?? "{}";
            }

            return new OfflineQueueItem
            {
                EventId = entity.EventId,
                EventType = entity.EventType,
                SequenceNumber = entity.SequenceNumber,
                ReliabilityClass = entity.ReliabilityClass,
                ContractVersion = entity.ContractVersion ?? "1.0",
                Payload = payloadObj,
                Timestamp = entity.OccurredAt
            };
        }
    }
}
