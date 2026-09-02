using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Application.Abstractions.Persistence
{
    public interface IConfigurationAssignmentRepository : IRepository<ConfigurationAssignment>
    {
        Task<ConfigurationAssignment?> GetAssignmentByPackageAndTargetAsync(Guid packageId, Guid targetId, CancellationToken cancellationToken = default);
        Task<List<ConfigurationAssignment>> GetAssignmentsForPackageAsync(Guid packageId, CancellationToken cancellationToken = default);
        Task<List<ConfigurationAssignment>> GetAssignmentsForTargetAsync(Guid targetId, CancellationToken cancellationToken = default);
        Task<List<ConfigurationAssignment>> GetApplicableAssignmentsAsync(Guid organizationId, Guid? siteId, List<Guid> groupIds, Guid workstationId, CancellationToken cancellationToken = default);
    }
}
