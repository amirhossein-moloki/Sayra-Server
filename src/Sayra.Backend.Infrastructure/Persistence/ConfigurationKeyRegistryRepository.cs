using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Domain;

#nullable enable

namespace Sayra.Backend.Infrastructure.Persistence
{
    public class ConfigurationKeyRegistryRepository : Repository<ConfigurationSigningKey>, IConfigurationKeyRegistryRepository
    {
        public ConfigurationKeyRegistryRepository(ApplicationDbContext dbContext) : base(dbContext)
        {
        }

        public async Task<ConfigurationSigningKey?> GetByKeyIdAsync(string keyId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(keyId)) return null;

            return await _dbSet.FirstOrDefaultAsync(k => k.KeyId == keyId.Trim(), cancellationToken);
        }

        public async Task<ConfigurationSigningKey?> GetActiveKeyAsync(CancellationToken cancellationToken = default)
        {
            return await _dbSet.FirstOrDefaultAsync(k => k.Status == SigningKeyStatus.Active, cancellationToken);
        }

        public async Task<List<ConfigurationSigningKey>> GetAllKeysAsync(CancellationToken cancellationToken = default)
        {
            return await _dbSet.OrderByDescending(k => k.CreatedAt).ToListAsync(cancellationToken);
        }
    }
}
