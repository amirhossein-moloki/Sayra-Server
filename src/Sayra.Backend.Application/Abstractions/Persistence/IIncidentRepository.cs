using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Domain.Enums;

namespace Sayra.Backend.Application.Abstractions.Persistence
{
    public interface IIncidentRepository
    {
        Task<Incident?> GetActiveIncidentByFingerprintAsync(string fingerprint, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<Incident>> GetActiveIncidentsForWorkstationAsync(string pcId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<Incident>> GetActiveIncidentsAsync(Guid? organizationId = null, Guid? siteId = null, CancellationToken cancellationToken = default);
        Task SaveIncidentAsync(Incident incident, CancellationToken cancellationToken = default);
        Task<Incident?> GetIncidentByIdAsync(Guid incidentId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<Incident>> GetIncidentsForOrganizationAsync(
            Guid organizationId,
            IncidentLifecycleState? state = null,
            int skip = 0,
            int take = 100,
            CancellationToken cancellationToken = default);

        Task<(IReadOnlyList<Incident> Items, int TotalCount)> QueryIncidentsAsync(
            Guid? organizationId = null,
            Guid? siteId = null,
            Guid? workstationId = null,
            string? pcId = null,
            AlertSeverity? severity = null,
            IncidentLifecycleState? state = null,
            string? ruleName = null,
            bool? activeOnly = null,
            int skip = 0,
            int take = 50,
            CancellationToken cancellationToken = default);
    }
}
