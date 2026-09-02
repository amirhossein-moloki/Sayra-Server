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
    public class ConfigurationPackageRepository : IConfigurationPackageRepository
    {
        private readonly ApplicationDbContext _dbContext;

        public ConfigurationPackageRepository(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        }

        public async Task<ConfigurationPackage?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return await _dbContext.ConfigurationPackages
                .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        }

        public async Task<ConfigurationPackage?> GetByPackageIdAndVersionAsync(string packageId, ConfigurationVersion version, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(packageId) || version is null)
                return null;

            var normalizedPackageId = packageId.Trim().ToUpperInvariant();
            var versionString = version.ToString();

            return await _dbContext.ConfigurationPackages
                .FirstOrDefaultAsync(p => p.PackageId == normalizedPackageId && p.Version == version, cancellationToken);
        }

        public async Task<IReadOnlyList<ConfigurationPackage>> GetPackagesByPackageIdAsync(string packageId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(packageId))
                return Array.Empty<ConfigurationPackage>();

            var normalizedPackageId = packageId.Trim().ToUpperInvariant();

            return await _dbContext.ConfigurationPackages
                .Where(p => p.PackageId == normalizedPackageId)
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<ConfigurationPackage>> GetActivePackagesByTargetIdAsync(Guid targetId, CancellationToken cancellationToken = default)
        {
            var activePackageIds = await _dbContext.ConfigurationAssignments
                .Where(a => a.ConfigurationTargetId == targetId && a.IsActive)
                .Select(a => a.ConfigurationPackageId)
                .ToListAsync(cancellationToken);

            return await _dbContext.ConfigurationPackages
                .Where(p => activePackageIds.Contains(p.Id))
                .ToListAsync(cancellationToken);
        }

        public async Task AddAsync(ConfigurationPackage package, CancellationToken cancellationToken = default)
        {
            await _dbContext.ConfigurationPackages.AddAsync(package, cancellationToken);
        }

        public Task UpdateAsync(ConfigurationPackage package, CancellationToken cancellationToken = default)
        {
            _dbContext.ConfigurationPackages.Update(package);
            return Task.CompletedTask;
        }
    }
}
