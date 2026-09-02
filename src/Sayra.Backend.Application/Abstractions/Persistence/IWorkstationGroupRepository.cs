using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Application.Abstractions.Persistence
{
    public interface IWorkstationGroupRepository : IRepository<WorkstationGroup>
    {
        Task<WorkstationGroup?> GetByCodeAsync(Guid organizationId, string code, CancellationToken cancellationToken = default);
        Task<List<Guid>> GetWorkstationGroupIdsForWorkstationAsync(Guid workstationId, CancellationToken cancellationToken = default);
        Task AddMemberAsync(WorkstationGroupMember member, CancellationToken cancellationToken = default);
        Task RemoveMemberAsync(Guid groupId, Guid workstationId, CancellationToken cancellationToken = default);
        Task<bool> IsMemberAsync(Guid groupId, Guid workstationId, CancellationToken cancellationToken = default);
    }
}
