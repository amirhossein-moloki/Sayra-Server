using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Application.OfflineQueue
{
    public interface IDeadLetterEventRepository
    {
        Task AddAsync(DeadLetterEvent entity, CancellationToken cancellationToken = default);
        Task<DeadLetterEvent?> GetByEventIdAsync(Guid eventId, CancellationToken cancellationToken = default);
        Task<DeadLetterEvent?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
        Task<(IReadOnlyList<DeadLetterEvent> Items, int TotalCount)> GetPagedAsync(
            string? clientId = null,
            string? siteId = null,
            Guid? organizationId = null,
            string? failureCode = null,
            string? processingStatus = null,
            int page = 1,
            int pageSize = 50,
            CancellationToken cancellationToken = default);
        Task UpdateAsync(DeadLetterEvent entity, CancellationToken cancellationToken = default);
    }
}
