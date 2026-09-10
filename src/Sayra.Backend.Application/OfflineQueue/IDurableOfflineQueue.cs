using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Application.OfflineQueue
{
    public interface IDurableOfflineQueue
    {
        Task<EnqueueResult> EnqueueAsync(ClientEventEnvelopeDto envelope, CancellationToken ct = default);
        Task<IReadOnlyList<DurableQueueItemEntity>> ClaimBatchAsync(int maxItems, CancellationToken ct = default);
        Task AcknowledgeItemsAsync(IEnumerable<string> eventIds, CancellationToken ct = default);
        Task MarkFailedAsync(string eventId, string errorReason, bool isPermanent = false, CancellationToken ct = default);
        Task<int> PurgeExpiredAsync(CancellationToken ct = default);
        Task<int> StartupRecoveryAsync(CancellationToken ct = default);
        Task<QueueMetricsDto> GetQueueMetricsAsync(CancellationToken ct = default);
    }
}
