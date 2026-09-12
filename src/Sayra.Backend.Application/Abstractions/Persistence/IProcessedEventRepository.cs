using System;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Application.Abstractions.Persistence
{
    public interface IProcessedEventRepository : IRepository<ProcessedEvent>
    {
        Task<ProcessedEvent?> GetByEventIdAsync(Guid eventId, bool track = true, CancellationToken cancellationToken = default);
    }
}
