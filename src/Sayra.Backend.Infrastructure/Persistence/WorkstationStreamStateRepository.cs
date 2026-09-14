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
    public class WorkstationStreamStateRepository : Repository<WorkstationStreamState>, IWorkstationStreamStateRepository
    {
        private readonly ApplicationDbContext _dbContext;

        public WorkstationStreamStateRepository(ApplicationDbContext dbContext) : base(dbContext)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        }

        public async Task<WorkstationStreamState?> GetByClientIdAsync(string clientId, bool track = true, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(clientId)) return null;

            string normalizedClientId = clientId.Trim().ToUpperInvariant();

            IQueryable<WorkstationStreamState> query = _dbContext.Set<WorkstationStreamState>();
            if (!track)
            {
                query = query.AsNoTracking();
            }

            return await query.FirstOrDefaultAsync(s => s.ClientId.ToUpper() == normalizedClientId, cancellationToken);
        }

        public async Task<List<ProcessedEvent>> GetPendingWaitingEventsAsync(string clientId, long expectedSequence, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(clientId)) return new List<ProcessedEvent>();

            string normalizedClientId = clientId.Trim().ToUpperInvariant();

            return await _dbContext.ProcessedEvents
                .Where(e => e.ClientId.ToUpper() == normalizedClientId &&
                            e.ProcessingStatus == "WAITING_FOR_SEQUENCE" &&
                            e.SequenceNumber == expectedSequence)
                .OrderBy(e => e.SequenceNumber)
                .Take(1000)
                .ToListAsync(cancellationToken);
        }
    }
}
