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
    public class TelemetryAggregateRepository : Repository<TelemetryAggregateRecord>, ITelemetryAggregateRepository
    {
        private const int MaxQueryLimit = 10000;
        private readonly ApplicationDbContext _appDbContext;

        public TelemetryAggregateRepository(ApplicationDbContext dbContext) : base(dbContext)
        {
            _appDbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        }

        public async Task<IReadOnlyList<TelemetryAggregateRecord>> GetAggregatesForWorkstationAsync(
            Guid workstationId,
            string granularity,
            DateTime? from = null,
            DateTime? to = null,
            int limit = 1000,
            CancellationToken cancellationToken = default)
        {
            if (workstationId == Guid.Empty)
            {
                return Array.Empty<TelemetryAggregateRecord>();
            }

            int boundedLimit = Math.Clamp(limit, 1, MaxQueryLimit);

            var query = _appDbContext.TelemetryAggregateRecords.AsNoTracking()
                .Where(t => t.WorkstationId == workstationId && t.Granularity == granularity);

            if (from.HasValue)
            {
                query = query.Where(t => t.WindowStart >= from.Value);
            }

            if (to.HasValue)
            {
                query = query.Where(t => t.WindowEnd <= to.Value);
            }

            return await query
                .OrderBy(t => t.WindowStart)
                .Take(boundedLimit)
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<TelemetryAggregateRecord>> GetAggregatesAllAsync(
            string granularity,
            DateTime? from = null,
            DateTime? to = null,
            int limit = 1000,
            CancellationToken cancellationToken = default)
        {
            int boundedLimit = Math.Clamp(limit, 1, MaxQueryLimit);

            var query = _appDbContext.TelemetryAggregateRecords.AsNoTracking()
                .Where(t => t.Granularity == granularity);

            if (from.HasValue)
            {
                query = query.Where(t => t.WindowStart >= from.Value);
            }

            if (to.HasValue)
            {
                query = query.Where(t => t.WindowEnd <= to.Value);
            }

            return await query
                .OrderBy(t => t.WindowStart)
                .Take(boundedLimit)
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<TelemetryAggregateRecord>> GetAggregatesForSiteAsync(
            Guid siteId,
            string granularity,
            DateTime? from = null,
            DateTime? to = null,
            int limit = 1000,
            CancellationToken cancellationToken = default)
        {
            if (siteId == Guid.Empty)
            {
                return Array.Empty<TelemetryAggregateRecord>();
            }

            int boundedLimit = Math.Clamp(limit, 1, MaxQueryLimit);

            var query = _appDbContext.TelemetryAggregateRecords.AsNoTracking()
                .Where(t => t.SiteId == siteId && t.Granularity == granularity);

            if (from.HasValue)
            {
                query = query.Where(t => t.WindowStart >= from.Value);
            }

            if (to.HasValue)
            {
                query = query.Where(t => t.WindowEnd <= to.Value);
            }

            return await query
                .OrderBy(t => t.WindowStart)
                .Take(boundedLimit)
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<TelemetryAggregateRecord>> GetAggregatesForOrganizationAsync(
            Guid organizationId,
            string granularity,
            DateTime? from = null,
            DateTime? to = null,
            int limit = 1000,
            CancellationToken cancellationToken = default)
        {
            if (organizationId == Guid.Empty)
            {
                return Array.Empty<TelemetryAggregateRecord>();
            }

            int boundedLimit = Math.Clamp(limit, 1, MaxQueryLimit);

            var query = _appDbContext.TelemetryAggregateRecords.AsNoTracking()
                .Where(t => t.OrganizationId == organizationId && t.Granularity == granularity);

            if (from.HasValue)
            {
                query = query.Where(t => t.WindowStart >= from.Value);
            }

            if (to.HasValue)
            {
                query = query.Where(t => t.WindowEnd <= to.Value);
            }

            return await query
                .OrderBy(t => t.WindowStart)
                .Take(boundedLimit)
                .ToListAsync(cancellationToken);
        }

        public async Task SaveAggregatesBatchAsync(
            IEnumerable<TelemetryAggregateRecord> aggregates,
            CancellationToken cancellationToken = default)
        {
            if (aggregates == null) return;

            var list = aggregates.ToList();
            if (list.Count == 0) return;

            // Group input batch by WorkstationId, Granularity, WindowStart to prevent duplicate tracker entities
            var groupedInput = list.GroupBy(r => (r.WorkstationId, r.Granularity, r.WindowStart));

            foreach (var group in groupedInput)
            {
                var key = group.Key;
                var records = group.ToList();

                // Check local EF tracker first
                var localEntity = _appDbContext.TelemetryAggregateRecords.Local
                    .FirstOrDefault(t => t.WorkstationId == key.WorkstationId &&
                                         t.Granularity == key.Granularity &&
                                         t.WindowStart == key.WindowStart);

                var existing = localEntity ?? await _appDbContext.TelemetryAggregateRecords
                    .FirstOrDefaultAsync(t => t.WorkstationId == key.WorkstationId &&
                                              t.Granularity == key.Granularity &&
                                              t.WindowStart == key.WindowStart,
                                              cancellationToken);

                foreach (var record in records)
                {
                    if (existing != null)
                    {
                        // Weighted merge with existing aggregate record
                        int totalSamples = existing.SampleCount + record.SampleCount;
                        if (totalSamples > 0)
                        {
                            existing.CpuAvg = (existing.CpuAvg * existing.SampleCount + record.CpuAvg * record.SampleCount) / totalSamples;
                            existing.RamAvg = (existing.RamAvg * existing.SampleCount + record.RamAvg * record.SampleCount) / totalSamples;
                        }

                        existing.SampleCount = totalSamples;
                        existing.IsPartial = record.IsPartial;

                        existing.CpuMin = Math.Min(existing.CpuMin, record.CpuMin);
                        existing.CpuMax = Math.Max(existing.CpuMax, record.CpuMax);
                        existing.CpuP50 = record.CpuP50;
                        existing.CpuP95 = record.CpuP95;
                        existing.CpuP99 = record.CpuP99;

                        existing.RamMin = Math.Min(existing.RamMin, record.RamMin);
                        existing.RamMax = Math.Max(existing.RamMax, record.RamMax);
                        existing.RamP50 = record.RamP50;
                        existing.RamP95 = record.RamP95;
                        existing.RamP99 = record.RamP99;

                        existing.UptimeDelta += record.UptimeDelta;
                        existing.LaunchesDelta += record.LaunchesDelta;
                        existing.CrashesDelta += record.CrashesDelta;
                        existing.RestartsDelta += record.RestartsDelta;

                        if (!string.IsNullOrWhiteSpace(record.PrimaryGameName))
                        {
                            existing.PrimaryGameName = record.PrimaryGameName;
                        }
                        existing.GameSampleCount += record.GameSampleCount;
                        if (record.GameCpuAvg.HasValue) existing.GameCpuAvg = record.GameCpuAvg;
                        if (record.GameCpuMax.HasValue) existing.GameCpuMax = Math.Max(existing.GameCpuMax ?? 0, record.GameCpuMax.Value);
                        if (record.GameRamAvg.HasValue) existing.GameRamAvg = record.GameRamAvg;
                        if (record.GameRamMax.HasValue) existing.GameRamMax = Math.Max(existing.GameRamMax ?? 0, record.GameRamMax.Value);
                        if (record.GameDurationMax.HasValue) existing.GameDurationMax = Math.Max(existing.GameDurationMax ?? 0, record.GameDurationMax.Value);

                        existing.UpdatedAt = DateTime.UtcNow;
                    }
                    else
                    {
                        await _appDbContext.TelemetryAggregateRecords.AddAsync(record, cancellationToken);
                        existing = record; // Now tracked locally
                    }
                }
            }

            await _appDbContext.SaveChangesAsync(cancellationToken);
        }

        public async Task<TelemetryAggregationCheckpoint?> GetCheckpointAsync(
            string granularity,
            CancellationToken cancellationToken = default)
        {
            return await _appDbContext.TelemetryAggregationCheckpoints
                .FirstOrDefaultAsync(c => c.Granularity == granularity, cancellationToken);
        }

        public async Task SaveCheckpointAsync(
            TelemetryAggregationCheckpoint checkpoint,
            CancellationToken cancellationToken = default)
        {
            if (checkpoint == null) return;

            var existing = await _appDbContext.TelemetryAggregationCheckpoints
                .FirstOrDefaultAsync(c => c.Granularity == checkpoint.Granularity, cancellationToken);

            if (existing != null)
            {
                existing.LastProcessedWindowEnd = checkpoint.LastProcessedWindowEnd;
                existing.LastProcessedServerTimestamp = checkpoint.LastProcessedServerTimestamp;
                existing.RecordsProcessed = checkpoint.RecordsProcessed;
                existing.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                await _appDbContext.TelemetryAggregationCheckpoints.AddAsync(checkpoint, cancellationToken);
            }

            await _appDbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
