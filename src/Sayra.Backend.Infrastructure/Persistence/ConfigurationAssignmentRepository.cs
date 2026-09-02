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
    public class ConfigurationAssignmentRepository : Repository<ConfigurationAssignment>, IConfigurationAssignmentRepository
    {
        public ConfigurationAssignmentRepository(ApplicationDbContext dbContext) : base(dbContext)
        {
        }

        public async Task<ConfigurationAssignment?> GetAssignmentByPackageAndTargetAsync(
            Guid packageId,
            Guid targetId,
            CancellationToken cancellationToken = default)
        {
            return await _dbSet.FirstOrDefaultAsync(a =>
                a.ConfigurationPackageId == packageId &&
                a.ConfigurationTargetId == targetId, cancellationToken);
        }

        public async Task<List<ConfigurationAssignment>> GetAssignmentsForPackageAsync(
            Guid packageId,
            CancellationToken cancellationToken = default)
        {
            return await _dbSet.Where(a => a.ConfigurationPackageId == packageId)
                .ToListAsync(cancellationToken);
        }

        public async Task<List<ConfigurationAssignment>> GetAssignmentsForTargetAsync(
            Guid targetId,
            CancellationToken cancellationToken = default)
        {
            return await _dbSet.Where(a => a.ConfigurationTargetId == targetId)
                .ToListAsync(cancellationToken);
        }

        public async Task<List<ConfigurationAssignment>> GetApplicableAssignmentsAsync(
            Guid organizationId,
            Guid? siteId,
            List<Guid> groupIds,
            Guid workstationId,
            CancellationToken cancellationToken = default)
        {
            groupIds ??= new List<Guid>();

            // Find target IDs belonging to the given organization and matching Global, Site, Group, or Workstation scopes
            var matchingTargetIds = await _context.Set<ConfigurationTarget>()
                .Where(t => t.OrganizationId == organizationId &&
                    (t.TargetType == ConfigurationTargetType.Global ||
                    (t.TargetType == ConfigurationTargetType.Site && siteId.HasValue && t.SiteId == siteId.Value) ||
                    (t.TargetType == ConfigurationTargetType.Group && t.GroupId.HasValue && groupIds.Contains(t.GroupId.Value)) ||
                    (t.TargetType == ConfigurationTargetType.Workstation && t.WorkstationId == workstationId)))
                .Select(t => t.Id)
                .ToListAsync(cancellationToken);

            if (!matchingTargetIds.Any())
            {
                return new List<ConfigurationAssignment>();
            }

            return await _dbSet
                .Where(a => a.IsActive && matchingTargetIds.Contains(a.ConfigurationTargetId))
                .ToListAsync(cancellationToken);
        }
    }
}
