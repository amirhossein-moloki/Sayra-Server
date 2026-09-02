using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Application.Abstractions.Persistence
{
    public interface IConfigurationPackageRepository
    {
        Task<ConfigurationPackage?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
        Task<ConfigurationPackage?> GetByPackageIdAndVersionAsync(string packageId, ConfigurationVersion version, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<ConfigurationPackage>> GetPackagesByPackageIdAsync(string packageId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<ConfigurationPackage>> GetActivePackagesByTargetIdAsync(Guid targetId, CancellationToken cancellationToken = default);
        Task AddAsync(ConfigurationPackage package, CancellationToken cancellationToken = default);
        Task UpdateAsync(ConfigurationPackage package, CancellationToken cancellationToken = default);
    }
}
