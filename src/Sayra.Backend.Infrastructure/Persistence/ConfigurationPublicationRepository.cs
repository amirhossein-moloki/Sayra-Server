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
    public class ConfigurationPublicationRepository : Repository<ConfigurationPublication>, IConfigurationPublicationRepository
    {
        private readonly ApplicationDbContext _dbContext;

        public ConfigurationPublicationRepository(ApplicationDbContext dbContext)
            : base(dbContext)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        }

        public async Task<ConfigurationPublication?> GetActivePublicationForTargetAsync(Guid targetId, CancellationToken cancellationToken = default)
        {
            return await _dbContext.ConfigurationPublications
                .Where(p => p.ConfigurationTargetId == targetId && p.Status == ConfigurationLifecycleState.Active)
                .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<ConfigurationPublication?> GetByPackageAndTargetAsync(Guid packageId, Guid targetId, CancellationToken cancellationToken = default)
        {
            return await _dbContext.ConfigurationPublications
                .Where(p => p.ConfigurationPackageId == packageId && p.ConfigurationTargetId == targetId)
                .OrderByDescending(p => p.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<ConfigurationPublication?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                return null;
            }

            var key = idempotencyKey.Trim();
            return await _dbContext.ConfigurationPublications
                .Where(p => p.IdempotencyKey == key)
                .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<List<ConfigurationPublication>> GetPublicationsForTargetAsync(Guid targetId, CancellationToken cancellationToken = default)
        {
            return await _dbContext.ConfigurationPublications
                .Where(p => p.ConfigurationTargetId == targetId)
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync(cancellationToken);
        }

        public async Task<List<ConfigurationPublication>> GetPublicationsForPackageAsync(Guid packageId, CancellationToken cancellationToken = default)
        {
            return await _dbContext.ConfigurationPublications
                .Where(p => p.ConfigurationPackageId == packageId)
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync(cancellationToken);
        }
    }
}
