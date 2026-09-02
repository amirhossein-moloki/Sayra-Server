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
    public class WorkstationGroupRepository : Repository<WorkstationGroup>, IWorkstationGroupRepository
    {
        public WorkstationGroupRepository(ApplicationDbContext dbContext) : base(dbContext)
        {
        }

        public async Task<WorkstationGroup?> GetByCodeAsync(Guid organizationId, string code, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;
            var normalizedCode = code.Trim().ToUpperInvariant();

            return await _dbSet.FirstOrDefaultAsync(g =>
                g.OrganizationId == organizationId && g.Code == normalizedCode, cancellationToken);
        }

        public async Task<List<Guid>> GetWorkstationGroupIdsForWorkstationAsync(Guid workstationId, CancellationToken cancellationToken = default)
        {
            return await _context.Set<WorkstationGroupMember>()
                .Where(m => m.WorkstationId == workstationId)
                .Select(m => m.WorkstationGroupId)
                .ToListAsync(cancellationToken);
        }

        public async Task AddMemberAsync(WorkstationGroupMember member, CancellationToken cancellationToken = default)
        {
            if (member == null) throw new ArgumentNullException(nameof(member));
            member.Validate();

            var exists = await IsMemberAsync(member.WorkstationGroupId, member.WorkstationId, cancellationToken);
            if (!exists)
            {
                await _context.Set<WorkstationGroupMember>().AddAsync(member, cancellationToken);
            }
        }

        public async Task RemoveMemberAsync(Guid groupId, Guid workstationId, CancellationToken cancellationToken = default)
        {
            var existing = await _context.Set<WorkstationGroupMember>()
                .FirstOrDefaultAsync(m => m.WorkstationGroupId == groupId && m.WorkstationId == workstationId, cancellationToken);

            if (existing != null)
            {
                _context.Set<WorkstationGroupMember>().Remove(existing);
            }
        }

        public async Task<bool> IsMemberAsync(Guid groupId, Guid workstationId, CancellationToken cancellationToken = default)
        {
            return await _context.Set<WorkstationGroupMember>()
                .AnyAsync(m => m.WorkstationGroupId == groupId && m.WorkstationId == workstationId, cancellationToken);
        }
    }
}
