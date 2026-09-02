using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Infrastructure.Persistence
{
    public class ConfigurationAssignmentRepository : IConfigurationAssignmentRepository
    {
        private readonly ApplicationDbContext _dbContext;

        public ConfigurationAssignmentRepository(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        }

        public async Task<ConfigurationAssignment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return await _dbContext.ConfigurationAssignments
                .Include(a => a.Package)
                .Include(a => a.Target)
                .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        }

        public async Task<IReadOnlyList<ConfigurationAssignment>> GetActiveAssignmentsByTargetIdAsync(Guid targetId, CancellationToken cancellationToken = default)
        {
            return await _dbContext.ConfigurationAssignments
                .Include(a => a.Package)
                .Include(a => a.Target)
                .Where(a => a.ConfigurationTargetId == targetId && a.IsActive)
                .OrderByDescending(a => a.Priority)
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<ConfigurationAssignment>> GetAssignmentsByPackageIdAsync(Guid packageId, CancellationToken cancellationToken = default)
        {
            return await _dbContext.ConfigurationAssignments
                .Include(a => a.Package)
                .Include(a => a.Target)
                .Where(a => a.ConfigurationPackageId == packageId)
                .ToListAsync(cancellationToken);
        }

        public async Task<ConfigurationAssignment?> GetActiveAssignmentAsync(Guid packageId, Guid targetId, CancellationToken cancellationToken = default)
        {
            return await _dbContext.ConfigurationAssignments
                .Include(a => a.Package)
                .Include(a => a.Target)
                .FirstOrDefaultAsync(a => a.ConfigurationPackageId == packageId && a.ConfigurationTargetId == targetId && a.IsActive, cancellationToken);
        }

        public async Task AddAsync(ConfigurationAssignment assignment, CancellationToken cancellationToken = default)
        {
            await _dbContext.ConfigurationAssignments.AddAsync(assignment, cancellationToken);
        }

        public Task UpdateAsync(ConfigurationAssignment assignment, CancellationToken cancellationToken = default)
        {
            _dbContext.ConfigurationAssignments.Update(assignment);
            return Task.CompletedTask;
        }
    }
}
