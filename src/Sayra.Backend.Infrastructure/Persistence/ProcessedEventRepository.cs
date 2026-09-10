using System;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Infrastructure.Persistence
{
    public class ProcessedEventRepository : Repository<ProcessedEvent>, IProcessedEventRepository
    {
        public ProcessedEventRepository(ApplicationDbContext dbContext) : base(dbContext)
        {
        }

        public Task<ProcessedEvent?> GetByEventIdAsync(Guid eventId, bool track = true, CancellationToken cancellationToken = default)
        {
            return FirstOrDefaultAsync(e => e.EventId == eventId, track, cancellationToken);
        }
    }
}
