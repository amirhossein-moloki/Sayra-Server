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
    public class UpdatePackageRepository : Repository<UpdatePackage>, IUpdatePackageRepository
    {
        public UpdatePackageRepository(ApplicationDbContext dbContext)
            : base(dbContext)
        {
        }

        public async Task<IReadOnlyList<UpdatePackage>> GetByReleaseIdAsync(Guid releaseId, bool track = true, CancellationToken cancellationToken = default)
        {
            if (releaseId == Guid.Empty)
            {
                return Array.Empty<UpdatePackage>();
            }

            var query = track ? _dbSet : _dbSet.AsNoTracking();

            return await query
                .Where(p => p.ReleaseId == releaseId)
                .OrderBy(p => p.CreatedAt)
                .ToListAsync(cancellationToken);
        }

        public async Task<UpdatePackage?> GetByStorageKeyAsync(string storageKey, bool track = true, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(storageKey))
            {
                return null;
            }

            var normalizedKey = storageKey.Trim();
            var query = track ? _dbSet : _dbSet.AsNoTracking();

            return await query.FirstOrDefaultAsync(p => p.StorageKey == normalizedKey, cancellationToken);
        }

        public async Task<UpdatePackage?> GetBySha256Async(string sha256, bool track = true, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sha256))
            {
                return null;
            }

            var normalizedHash = sha256.Trim().ToLowerInvariant();
            var query = track ? _dbSet : _dbSet.AsNoTracking();

            return await query.FirstOrDefaultAsync(p => p.SHA256 == normalizedHash, cancellationToken);
        }
    }
}
