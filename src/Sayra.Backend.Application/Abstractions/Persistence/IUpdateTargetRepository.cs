using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Domain;

#nullable enable

namespace Sayra.Backend.Application.Abstractions.Persistence
{
    public interface IUpdateTargetRepository : IRepository<UpdateTarget>
    {
        Task<IReadOnlyList<UpdateTarget>> GetByReleaseIdAsync(Guid releaseId, bool track = false, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<UpdateTarget>> GetByOrganizationIdAsync(Guid organizationId, bool track = false, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<UpdateTarget>> GetApplicableTargetsAsync(
            Guid organizationId,
            Guid? siteId,
            IEnumerable<Guid> groupIds,
            Guid workstationId,
            bool track = false,
            CancellationToken cancellationToken = default);
        Task<UpdateTarget?> GetByScopeAsync(
            Guid organizationId,
            Guid releaseId,
            ConfigurationTargetType targetType,
            Guid? siteId,
            Guid? groupId,
            Guid? workstationId,
            CancellationToken cancellationToken = default);
    }
}
