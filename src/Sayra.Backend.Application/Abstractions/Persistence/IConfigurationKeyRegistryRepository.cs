using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Domain;

#nullable enable

namespace Sayra.Backend.Application.Abstractions.Persistence
{
    public interface IConfigurationKeyRegistryRepository : IRepository<ConfigurationSigningKey>
    {
        Task<ConfigurationSigningKey?> GetByKeyIdAsync(string keyId, CancellationToken cancellationToken = default);
        Task<ConfigurationSigningKey?> GetActiveKeyAsync(CancellationToken cancellationToken = default);
        Task<List<ConfigurationSigningKey>> GetAllKeysAsync(CancellationToken cancellationToken = default);
    }
}
