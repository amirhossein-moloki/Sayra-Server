using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Application.OfflineQueue
{
    public class EnqueueResult
    {
        public bool IsQueued { get; init; }
        public string Reason { get; init; } = string.Empty;
        public DurableQueueItemEntity? Item { get; init; }

        public static EnqueueResult Success(DurableQueueItemEntity item) =>
            new EnqueueResult { IsQueued = true, Reason = "Success", Item = item };

        public static EnqueueResult Rejected(string reason) =>
            new EnqueueResult { IsQueued = false, Reason = reason, Item = null };
    }
}
