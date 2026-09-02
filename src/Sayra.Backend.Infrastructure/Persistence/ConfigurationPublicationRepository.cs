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
    public class ConfigurationPublicationRepository : IConfigurationPublicationRepository
    {
        private readonly ApplicationDbContext _dbContext;

        public ConfigurationPublicationRepository(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        }

        public async Task<ConfigurationPublication?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return await _dbContext.ConfigurationPublications
                .Include(p => p.Package)
                .Include(p => p.Target)
                .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        }

        public async Task<IReadOnlyList<ConfigurationPublication>> GetPublicationsByPackageIdAsync(Guid packageId, CancellationToken cancellationToken = default)
        {
            return await _dbContext.ConfigurationPublications
                .Include(p => p.Package)
                .Include(p => p.Target)
                .Where(p => p.ConfigurationPackageId == packageId)
                .OrderByDescending(p => p.PublishedAt)
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<ConfigurationPublication>> GetPublicationsByTargetIdAsync(Guid targetId, CancellationToken cancellationToken = default)
        {
            return await _dbContext.ConfigurationPublications
                .Include(p => p.Package)
                .Include(p => p.Target)
                .Where(p => p.ConfigurationTargetId == targetId)
                .OrderByDescending(p => p.PublishedAt)
                .ToListAsync(cancellationToken);
        }

        public async Task AddAsync(ConfigurationPublication publication, CancellationToken cancellationToken = default)
        {
            await _dbContext.ConfigurationPublications.AddAsync(publication, cancellationToken);
        }

        public Task UpdateAsync(ConfigurationPublication publication, CancellationToken cancellationToken = default)
        {
            _dbContext.ConfigurationPublications.Update(publication);
            return Task.CompletedTask;
        }
    }
}
