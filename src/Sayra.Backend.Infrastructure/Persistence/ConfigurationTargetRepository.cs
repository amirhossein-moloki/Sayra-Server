using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Infrastructure.Persistence
{
    public class ConfigurationTargetRepository : IConfigurationTargetRepository
    {
        private readonly ApplicationDbContext _dbContext;

        public ConfigurationTargetRepository(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        }

        public async Task<ConfigurationTarget?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return await _dbContext.ConfigurationTargets
                .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        }

        public async Task<ConfigurationTarget?> GetByTypeAndIdentifierAsync(ConfigurationTargetType targetType, string identifier, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(identifier))
                return null;

            var normalizedIdentifier = identifier.Trim().ToUpperInvariant();

            return await _dbContext.ConfigurationTargets
                .FirstOrDefaultAsync(t => t.TargetType == targetType && t.TargetIdentifier == normalizedIdentifier, cancellationToken);
        }

        public async Task<IReadOnlyList<ConfigurationTarget>> GetByTargetTypeAsync(ConfigurationTargetType targetType, CancellationToken cancellationToken = default)
        {
            return await _dbContext.ConfigurationTargets
                .Where(t => t.TargetType == targetType)
                .ToListAsync(cancellationToken);
        }

        public async Task AddAsync(ConfigurationTarget target, CancellationToken cancellationToken = default)
        {
            await _dbContext.ConfigurationTargets.AddAsync(target, cancellationToken);
        }

        public Task UpdateAsync(ConfigurationTarget target, CancellationToken cancellationToken = default)
        {
            _dbContext.ConfigurationTargets.Update(target);
            return Task.CompletedTask;
        }
    }
}
