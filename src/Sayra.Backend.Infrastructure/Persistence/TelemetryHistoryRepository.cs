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
    public class TelemetryHistoryRepository : Repository<TelemetryHistoryRecord>, ITelemetryHistoryRepository
    {
        private const int MaxQueryLimit = 10000;
        private readonly ApplicationDbContext _appDbContext;

        public TelemetryHistoryRepository(ApplicationDbContext dbContext)
            : base(dbContext)
        {
            _appDbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        }

        public async Task<IReadOnlyList<TelemetryHistoryRecord>> GetHistoryForWorkstationAsync(
            Guid workstationId,
            DateTime? from = null,
            DateTime? to = null,
            int limit = 1000,
            CancellationToken cancellationToken = default)
        {
            if (workstationId == Guid.Empty)
            {
                return Array.Empty<TelemetryHistoryRecord>();
            }

            int boundedLimit = Math.Clamp(limit, 1, MaxQueryLimit);

            var query = _appDbContext.TelemetryHistoryRecords.AsNoTracking()
                .Where(t => t.WorkstationId == workstationId);

            if (from.HasValue)
            {
                query = query.Where(t => t.ServerReceivedAt >= from.Value);
            }

            if (to.HasValue)
            {
                query = query.Where(t => t.ServerReceivedAt <= to.Value);
            }

            return await query
                .OrderByDescending(t => t.ServerReceivedAt)
                .Take(boundedLimit)
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<TelemetryHistoryRecord>> GetHistoryAllAsync(
            DateTime? from = null,
            DateTime? to = null,
            int limit = 1000,
            CancellationToken cancellationToken = default)
        {
            int boundedLimit = Math.Clamp(limit, 1, MaxQueryLimit);

            var query = _appDbContext.TelemetryHistoryRecords.AsNoTracking();

            if (from.HasValue)
            {
                query = query.Where(t => t.ServerReceivedAt >= from.Value);
            }

            if (to.HasValue)
            {
                query = query.Where(t => t.ServerReceivedAt <= to.Value);
            }

            return await query
                .OrderBy(t => t.ServerReceivedAt)
                .Take(boundedLimit)
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<TelemetryHistoryRecord>> GetHistoryForSiteAsync(
            Guid siteId,
            DateTime? from = null,
            DateTime? to = null,
            int limit = 1000,
            CancellationToken cancellationToken = default)
        {
            if (siteId == Guid.Empty)
            {
                return Array.Empty<TelemetryHistoryRecord>();
            }

            int boundedLimit = Math.Clamp(limit, 1, MaxQueryLimit);

            var query = _appDbContext.TelemetryHistoryRecords.AsNoTracking()
                .Where(t => t.SiteId == siteId);

            if (from.HasValue)
            {
                query = query.Where(t => t.ServerReceivedAt >= from.Value);
            }

            if (to.HasValue)
            {
                query = query.Where(t => t.ServerReceivedAt <= to.Value);
            }

            return await query
                .OrderByDescending(t => t.ServerReceivedAt)
                .Take(boundedLimit)
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<TelemetryHistoryRecord>> GetHistoryForOrganizationAsync(
            Guid organizationId,
            DateTime? from = null,
            DateTime? to = null,
            int limit = 1000,
            CancellationToken cancellationToken = default)
        {
            if (organizationId == Guid.Empty)
            {
                return Array.Empty<TelemetryHistoryRecord>();
            }

            int boundedLimit = Math.Clamp(limit, 1, MaxQueryLimit);

            var query = _appDbContext.TelemetryHistoryRecords.AsNoTracking()
                .Where(t => t.OrganizationId == organizationId);

            if (from.HasValue)
            {
                query = query.Where(t => t.ServerReceivedAt >= from.Value);
            }

            if (to.HasValue)
            {
                query = query.Where(t => t.ServerReceivedAt <= to.Value);
            }

            return await query
                .OrderByDescending(t => t.ServerReceivedAt)
                .Take(boundedLimit)
                .ToListAsync(cancellationToken);
        }

        public async Task AddHeartbeatRecordAsync(HeartbeatHistoryRecord record, CancellationToken cancellationToken = default)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            await _appDbContext.HeartbeatHistoryRecords.AddAsync(record, cancellationToken);
        }

        public async Task<IReadOnlyList<HeartbeatHistoryRecord>> GetHeartbeatsForWorkstationAsync(
            Guid workstationId,
            DateTime? from = null,
            DateTime? to = null,
            int limit = 1000,
            CancellationToken cancellationToken = default)
        {
            if (workstationId == Guid.Empty)
            {
                return Array.Empty<HeartbeatHistoryRecord>();
            }

            int boundedLimit = Math.Clamp(limit, 1, MaxQueryLimit);

            var query = _appDbContext.HeartbeatHistoryRecords.AsNoTracking()
                .Where(h => h.WorkstationId == workstationId);

            if (from.HasValue)
            {
                query = query.Where(h => h.ServerReceivedAt >= from.Value);
            }

            if (to.HasValue)
            {
                query = query.Where(h => h.ServerReceivedAt <= to.Value);
            }

            return await query
                .OrderByDescending(h => h.ServerReceivedAt)
                .Take(boundedLimit)
                .ToListAsync(cancellationToken);
        }
    }
}
