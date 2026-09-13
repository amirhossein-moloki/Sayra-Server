using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sayra.Backend.Application.OfflineQueue;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Infrastructure.Persistence
{
    public class DeadLetterEventRepository : IDeadLetterEventRepository
    {
        private readonly ApplicationDbContext _dbContext;

        public DeadLetterEventRepository(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        }

        public async Task AddAsync(DeadLetterEvent entity, CancellationToken cancellationToken = default)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            await _dbContext.DeadLetterEvents.AddAsync(entity, cancellationToken);
        }

        public async Task<DeadLetterEvent?> GetByEventIdAsync(Guid eventId, CancellationToken cancellationToken = default)
        {
            return await _dbContext.DeadLetterEvents
                .FirstOrDefaultAsync(x => x.EventId == eventId, cancellationToken);
        }

        public async Task<DeadLetterEvent?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return await _dbContext.DeadLetterEvents
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        }

        public async Task<(IReadOnlyList<DeadLetterEvent> Items, int TotalCount)> GetPagedAsync(
            string? clientId = null,
            string? siteId = null,
            Guid? organizationId = null,
            string? failureCode = null,
            string? processingStatus = null,
            int page = 1,
            int pageSize = 50,
            CancellationToken cancellationToken = default)
        {
            var query = _dbContext.DeadLetterEvents.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(clientId))
            {
                query = query.Where(x => x.ClientId == clientId);
            }

            if (!string.IsNullOrWhiteSpace(siteId))
            {
                query = query.Where(x => x.SiteId == siteId);
            }

            if (organizationId.HasValue && organizationId.Value != Guid.Empty)
            {
                query = query.Where(x => x.OrganizationId == organizationId.Value);
            }

            if (!string.IsNullOrWhiteSpace(failureCode))
            {
                query = query.Where(x => x.FailureCode == failureCode);
            }

            if (!string.IsNullOrWhiteSpace(processingStatus))
            {
                query = query.Where(x => x.ProcessingStatus == processingStatus);
            }

            int totalCount = await query.CountAsync(cancellationToken);

            var items = await query
                .OrderByDescending(x => x.DeadLetteredAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            return (items.AsReadOnly(), totalCount);
        }

        public Task UpdateAsync(DeadLetterEvent entity, CancellationToken cancellationToken = default)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            _dbContext.DeadLetterEvents.Update(entity);
            return Task.CompletedTask;
        }
    }
}
