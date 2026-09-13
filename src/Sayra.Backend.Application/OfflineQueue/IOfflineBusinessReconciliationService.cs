using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Application.OfflineQueue
{
    public interface IOfflineBusinessReconciliationService
    {
        Task<BusinessReconciliationResult> ReconcileAsync(
            OfflineQueueItem item,
            Workstation workstation,
            Site? site,
            string batchId,
            CancellationToken cancellationToken = default);
    }
}
