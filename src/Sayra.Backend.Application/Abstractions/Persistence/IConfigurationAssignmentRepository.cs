using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Application.Abstractions.Persistence
{
    public interface IConfigurationAssignmentRepository
    {
        Task<ConfigurationAssignment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<ConfigurationAssignment>> GetActiveAssignmentsByTargetIdAsync(Guid targetId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<ConfigurationAssignment>> GetAssignmentsByPackageIdAsync(Guid packageId, CancellationToken cancellationToken = default);
        Task<ConfigurationAssignment?> GetActiveAssignmentAsync(Guid packageId, Guid targetId, CancellationToken cancellationToken = default);
        Task AddAsync(ConfigurationAssignment assignment, CancellationToken cancellationToken = default);
        Task UpdateAsync(ConfigurationAssignment assignment, CancellationToken cancellationToken = default);
    }
}
