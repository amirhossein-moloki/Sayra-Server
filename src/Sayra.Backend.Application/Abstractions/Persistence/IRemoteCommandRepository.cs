using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Application.Abstractions.Persistence
{
    public interface IRemoteCommandRepository
    {
        Task<RemoteCommand?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
        Task<RemoteCommand?> GetByCommandIdAsync(string commandId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<RemoteCommand>> GetPendingCommandsByWorkstationAsync(Guid workstationId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<RemoteCommand>> GetActiveCommandsByPcIdAsync(string pcId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<RemoteCommand>> GetExpiredCommandsAsync(DateTime cutoffTime, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<RemoteCommand>> GetTimedOutDeliveryCommandsAsync(DateTime cutoffTime, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<RemoteCommand>> GetTimedOutExecutionCommandsAsync(DateTime cutoffTime, CancellationToken cancellationToken = default);
        Task AddAsync(RemoteCommand command, CancellationToken cancellationToken = default);
        Task UpdateAsync(RemoteCommand command, CancellationToken cancellationToken = default);
    }
}
