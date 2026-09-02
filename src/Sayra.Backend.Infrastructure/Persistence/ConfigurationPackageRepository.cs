using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Infrastructure.Persistence
{
    public class ConfigurationPackageRepository : Repository<ConfigurationPackage>, IConfigurationPackageRepository
    {
        private readonly ApplicationDbContext _dbContext;

        public ConfigurationPackageRepository(ApplicationDbContext dbContext) : base(dbContext)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        }

        public async Task<ConfigurationPackage?> GetLatestVersionAsync(string name, CancellationToken cancellationToken = default)
        {
            var normalizedName = (name ?? "default").Trim();
            return await _dbContext.ConfigurationPackages
                .Where(c => c.Name == normalizedName)
                .OrderByDescending(c => c.VersionNumber)
                .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<ConfigurationPackage?> GetByVersionNumberAsync(string name, long versionNumber, CancellationToken cancellationToken = default)
        {
            var normalizedName = (name ?? "default").Trim();
            return await _dbContext.ConfigurationPackages
                .FirstOrDefaultAsync(c => c.Name == normalizedName && c.VersionNumber == versionNumber, cancellationToken);
        }

        public async Task<List<ConfigurationPackage>> GetVersionRangeAsync(string name, long startVersionNumber, long endVersionNumber, CancellationToken cancellationToken = default)
        {
            var normalizedName = (name ?? "default").Trim();
            return await _dbContext.ConfigurationPackages
                .Where(c => c.Name == normalizedName && c.VersionNumber >= startVersionNumber && c.VersionNumber <= endVersionNumber)
                .OrderBy(c => c.VersionNumber)
                .ToListAsync(cancellationToken);
        }
    }
}
