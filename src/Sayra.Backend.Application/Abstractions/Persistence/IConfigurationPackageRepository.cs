using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Application.Abstractions.Persistence
{
    public interface IConfigurationPackageRepository : IRepository<ConfigurationPackage>
    {
        Task<ConfigurationPackage?> GetLatestVersionAsync(string name, CancellationToken cancellationToken = default);
        Task<ConfigurationPackage?> GetByVersionNumberAsync(string name, long versionNumber, CancellationToken cancellationToken = default);
        Task<List<ConfigurationPackage>> GetVersionRangeAsync(string name, long startVersionNumber, long endVersionNumber, CancellationToken cancellationToken = default);
    }
}
