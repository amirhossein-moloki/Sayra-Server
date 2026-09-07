using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Application.Abstractions.Persistence
{
    public interface ITelemetryAggregateRepository : IRepository<TelemetryAggregateRecord>
    {
        Task<IReadOnlyList<TelemetryAggregateRecord>> GetAggregatesForWorkstationAsync(
            Guid workstationId,
            string granularity,
            DateTime? from = null,
            DateTime? to = null,
            int limit = 1000,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<TelemetryAggregateRecord>> GetAggregatesAllAsync(
            string granularity,
            DateTime? from = null,
            DateTime? to = null,
            int limit = 1000,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<TelemetryAggregateRecord>> GetAggregatesForSiteAsync(
            Guid siteId,
            string granularity,
            DateTime? from = null,
            DateTime? to = null,
            int limit = 1000,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<TelemetryAggregateRecord>> GetAggregatesForOrganizationAsync(
            Guid organizationId,
            string granularity,
            DateTime? from = null,
            DateTime? to = null,
            int limit = 1000,
            CancellationToken cancellationToken = default);

        Task SaveAggregatesBatchAsync(
            IEnumerable<TelemetryAggregateRecord> aggregates,
            CancellationToken cancellationToken = default);

        Task<TelemetryAggregationCheckpoint?> GetCheckpointAsync(
            string granularity,
            CancellationToken cancellationToken = default);

        Task SaveCheckpointAsync(
            TelemetryAggregationCheckpoint checkpoint,
            CancellationToken cancellationToken = default);
    }
}
