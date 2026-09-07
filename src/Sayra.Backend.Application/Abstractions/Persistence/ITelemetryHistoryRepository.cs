using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Application.Abstractions.Persistence
{
    public interface ITelemetryHistoryRepository : IRepository<TelemetryHistoryRecord>
    {
        Task<IReadOnlyList<TelemetryHistoryRecord>> GetHistoryForWorkstationAsync(
            Guid workstationId,
            DateTime? from = null,
            DateTime? to = null,
            int limit = 1000,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<TelemetryHistoryRecord>> GetHistoryForSiteAsync(
            Guid siteId,
            DateTime? from = null,
            DateTime? to = null,
            int limit = 1000,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<TelemetryHistoryRecord>> GetHistoryForOrganizationAsync(
            Guid organizationId,
            DateTime? from = null,
            DateTime? to = null,
            int limit = 1000,
            CancellationToken cancellationToken = default);

        Task AddHeartbeatRecordAsync(HeartbeatHistoryRecord record, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<HeartbeatHistoryRecord>> GetHeartbeatsForWorkstationAsync(
            Guid workstationId,
            DateTime? from = null,
            DateTime? to = null,
            int limit = 1000,
            CancellationToken cancellationToken = default);
    }
}
