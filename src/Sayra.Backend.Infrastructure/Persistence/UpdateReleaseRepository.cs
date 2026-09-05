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
    public class UpdateReleaseRepository : Repository<UpdateRelease>, IUpdateReleaseRepository
    {
        public UpdateReleaseRepository(ApplicationDbContext dbContext)
            : base(dbContext)
        {
        }

        public async Task<UpdateRelease?> GetByOrganizationAndVersionAsync(Guid organizationId, string version, bool track = true, CancellationToken cancellationToken = default)
        {
            if (organizationId == Guid.Empty || string.IsNullOrWhiteSpace(version))
            {
                return null;
            }

            var normalizedVersion = version.Trim();
            var query = track ? _dbSet : _dbSet.AsNoTracking();

            return await query
                .Include(r => r.Packages)
                .FirstOrDefaultAsync(r => r.OrganizationId == organizationId && r.Version == normalizedVersion, cancellationToken);
        }

        public async Task<IReadOnlyList<UpdateRelease>> GetByOrganizationIdAsync(Guid organizationId, bool track = true, CancellationToken cancellationToken = default)
        {
            if (organizationId == Guid.Empty)
            {
                return Array.Empty<UpdateRelease>();
            }

            var query = track ? _dbSet : _dbSet.AsNoTracking();

            return await query
                .Include(r => r.Packages)
                .Where(r => r.OrganizationId == organizationId)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync(cancellationToken);
        }

        public async Task<UpdateRelease?> GetActiveReleaseAsync(Guid organizationId, bool track = true, CancellationToken cancellationToken = default)
        {
            if (organizationId == Guid.Empty)
            {
                return null;
            }

            var query = track ? _dbSet : _dbSet.AsNoTracking();

            return await query
                .Include(r => r.Packages)
                .FirstOrDefaultAsync(r => r.OrganizationId == organizationId && r.Status == UpdateReleaseStatus.Active, cancellationToken);
        }

        public override async Task<UpdateRelease?> GetByIdAsync(Guid id, bool track = true, CancellationToken cancellationToken = default)
        {
            var query = track ? _dbSet : _dbSet.AsNoTracking();
            return await query
                .Include(r => r.Packages)
                .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        }
    }
}
