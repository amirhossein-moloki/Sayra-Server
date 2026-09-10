using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Contracts;

namespace Sayra.Backend.Application.OfflineQueue
{
    public interface IOfflineSyncWorker
    {
        bool IsRunning { get; }
        Task<OfflineBatchAcknowledgment?> SynchronizeBatchAsync(
            string clientId,
            string workstationId,
            System.Func<OfflineBatchRequest, CancellationToken, Task<OfflineBatchAcknowledgment?>> sendBatchFunc,
            CancellationToken cancellationToken = default);
    }
}
