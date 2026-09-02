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
    public class ConfigurationTargetRepository : Repository<ConfigurationTarget>, IConfigurationTargetRepository
    {
        public ConfigurationTargetRepository(ApplicationDbContext dbContext) : base(dbContext)
        {
        }

        public async Task<ConfigurationTarget?> GetByScopeAsync(
            ConfigurationTargetType targetType,
            Guid organizationId,
            Guid? siteId,
            Guid? groupId,
            Guid? workstationId,
            CancellationToken cancellationToken = default)
        {
            return await _dbSet.FirstOrDefaultAsync(t =>
                t.TargetType == targetType &&
                t.OrganizationId == organizationId &&
                t.SiteId == siteId &&
                t.GroupId == groupId &&
                t.WorkstationId == workstationId, cancellationToken);
        }

        public async Task<List<ConfigurationTarget>> GetTargetsForOrganizationAsync(
            Guid organizationId,
            CancellationToken cancellationToken = default)
        {
            return await _dbSet.Where(t => t.OrganizationId == organizationId)
                .ToListAsync(cancellationToken);
        }
    }
}
