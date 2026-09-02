using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Application.Abstractions.Persistence
{
    public interface IConfigurationTargetRepository : IRepository<ConfigurationTarget>
    {
        Task<ConfigurationTarget?> GetByScopeAsync(ConfigurationTargetType targetType, Guid organizationId, Guid? siteId, Guid? groupId, Guid? workstationId, CancellationToken cancellationToken = default);
        Task<List<ConfigurationTarget>> GetTargetsForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default);
    }
}
