using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Contracts;

namespace Sayra.Backend.Application.OfflineQueue
{
    public interface IOfflineOrderingAndReconciliationEngine
    {
        Task<OrderingAndReconciliationResult> EvaluateAndReconcileAsync(
            OfflineQueueItem item,
            string authenticatedPcId,
            string batchId,
            CancellationToken cancellationToken = default);
    }
}
