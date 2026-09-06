using System;
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
    public class UpdateTargetRepository : Repository<UpdateTarget>, IUpdateTargetRepository
    {
        public UpdateTargetRepository(ApplicationDbContext dbContext) : base(dbContext)
        {
        }

        public async Task<IReadOnlyList<UpdateTarget>> GetByReleaseIdAsync(
            Guid releaseId,
            bool track = false,
            CancellationToken cancellationToken = default)
        {
            var query = track ? _dbSet : _dbSet.AsNoTracking();

            return await query
                .Include(t => t.Release)
                .ThenInclude(r => r!.Packages)
                .Where(t => t.ReleaseId == releaseId)
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<UpdateTarget>> GetByOrganizationIdAsync(
            Guid organizationId,
            bool track = false,
            CancellationToken cancellationToken = default)
        {
            var query = track ? _dbSet : _dbSet.AsNoTracking();

            return await query
                .Include(t => t.Release)
                .ThenInclude(r => r!.Packages)
                .Where(t => t.OrganizationId == organizationId)
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<UpdateTarget>> GetApplicableTargetsAsync(
            Guid organizationId,
            Guid? siteId,
            IEnumerable<Guid> groupIds,
            Guid workstationId,
            bool track = false,
            CancellationToken cancellationToken = default)
        {
            var query = track ? _dbSet : _dbSet.AsNoTracking();
            var groupList = groupIds?.ToList() ?? new List<Guid>();

            return await query
                .Include(t => t.Release)
                .ThenInclude(r => r!.Packages)
                .Where(t => t.OrganizationId == organizationId && t.IsEnabled)
                .Where(t =>
                    t.TargetType == ConfigurationTargetType.Global ||
                    (t.TargetType == ConfigurationTargetType.Site && siteId.HasValue && t.SiteId == siteId.Value) ||
                    (t.TargetType == ConfigurationTargetType.Group && t.GroupId.HasValue && groupList.Contains(t.GroupId.Value)) ||
                    (t.TargetType == ConfigurationTargetType.Workstation && t.WorkstationId == workstationId)
                )
                .ToListAsync(cancellationToken);
        }

        public async Task<UpdateTarget?> GetByScopeAsync(
            Guid organizationId,
            Guid releaseId,
            ConfigurationTargetType targetType,
            Guid? siteId,
            Guid? groupId,
            Guid? workstationId,
            CancellationToken cancellationToken = default)
        {
            return await _dbSet.FirstOrDefaultAsync(t =>
                t.OrganizationId == organizationId &&
                t.ReleaseId == releaseId &&
                t.TargetType == targetType &&
                t.SiteId == siteId &&
                t.GroupId == groupId &&
                t.WorkstationId == workstationId,
                cancellationToken);
        }
    }
}
