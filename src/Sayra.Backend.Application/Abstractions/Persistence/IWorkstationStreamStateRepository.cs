using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Application.Abstractions.Persistence
{
    public interface IWorkstationStreamStateRepository : IRepository<WorkstationStreamState>
    {
        Task<WorkstationStreamState?> GetByClientIdAsync(string clientId, bool track = true, CancellationToken cancellationToken = default);
        Task<List<ProcessedEvent>> GetPendingWaitingEventsAsync(string clientId, long expectedSequence, CancellationToken cancellationToken = default);
    }
}
