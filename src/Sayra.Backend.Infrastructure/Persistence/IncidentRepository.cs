using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Domain.Enums;

namespace Sayra.Backend.Infrastructure.Persistence
{
    public class IncidentRepository : IIncidentRepository
    {
        private readonly ApplicationDbContext _context;

        public IncidentRepository(ApplicationDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public async Task<Incident?> GetActiveIncidentByFingerprintAsync(string fingerprint, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(fingerprint))
            {
                return null;
            }

            return await _context.Incidents
                .FirstOrDefaultAsync(i => i.Fingerprint == fingerprint && i.LifecycleState != IncidentLifecycleState.Resolved, cancellationToken);
        }

        public async Task<IReadOnlyList<Incident>> GetActiveIncidentsForWorkstationAsync(string pcId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(pcId))
            {
                return Array.Empty<Incident>();
            }

            return await _context.Incidents
                .Where(i => i.PcId == pcId && i.LifecycleState != IncidentLifecycleState.Resolved)
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<Incident>> GetActiveIncidentsAsync(Guid? organizationId = null, Guid? siteId = null, CancellationToken cancellationToken = default)
        {
            var query = _context.Incidents
                .Where(i => i.LifecycleState != IncidentLifecycleState.Resolved);

            if (organizationId.HasValue)
            {
                query = query.Where(i => i.OrganizationId == organizationId.Value);
            }

            if (siteId.HasValue)
            {
                query = query.Where(i => i.SiteId == siteId.Value);
            }

            return await query.ToListAsync(cancellationToken);
        }

        public async Task SaveIncidentAsync(Incident incident, CancellationToken cancellationToken = default)
        {
            if (incident == null)
            {
                throw new ArgumentNullException(nameof(incident));
            }

            var existing = await _context.Incidents.FindAsync(new object[] { incident.Id }, cancellationToken);
            if (existing == null)
            {
                await _context.Incidents.AddAsync(incident, cancellationToken);
            }

            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task<Incident?> GetIncidentByIdAsync(Guid incidentId, CancellationToken cancellationToken = default)
        {
            return await _context.Incidents.FindAsync(new object[] { incidentId }, cancellationToken);
        }

        public async Task<IReadOnlyList<Incident>> GetIncidentsForOrganizationAsync(
            Guid organizationId,
            IncidentLifecycleState? state = null,
            int skip = 0,
            int take = 100,
            CancellationToken cancellationToken = default)
        {
            var query = _context.Incidents
                .Where(i => i.OrganizationId == organizationId);

            if (state.HasValue)
            {
                query = query.Where(i => i.LifecycleState == state.Value);
            }

            int safeTake = Math.Clamp(take, 1, 1000);
            int safeSkip = Math.Max(0, skip);

            return await query
                .OrderByDescending(i => i.LastObservedAtUtc)
                .Skip(safeSkip)
                .Take(safeTake)
                .ToListAsync(cancellationToken);
        }
    }
}
