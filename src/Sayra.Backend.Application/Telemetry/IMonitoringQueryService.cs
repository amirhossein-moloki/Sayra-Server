using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Domain.Enums;
using Sayra.Backend.Shared;

namespace Sayra.Backend.Application.Telemetry
{
    public interface IMonitoringQueryService
    {
        Task<Result<FleetWorkstationsResponseDto>> GetFleetWorkstationsAsync(
            UserPrincipal principal,
            Guid? siteId = null,
            Guid? organizationId = null,
            WorkstationHealthState? healthState = null,
            string? connectionState = null,
            string? status = null,
            int page = 1,
            int pageSize = 50,
            CancellationToken cancellationToken = default);

        Task<Result<WorkstationDetailDto>> GetWorkstationDetailAsync(
            UserPrincipal principal,
            Guid workstationId,
            CancellationToken cancellationToken = default);

        Task<Result<WorkstationHealthDetailDto>> GetWorkstationHealthAsync(
            UserPrincipal principal,
            Guid workstationId,
            CancellationToken cancellationToken = default);

        Task<Result<HistoricalMetricsResponseDto>> GetWorkstationMetricsAsync(
            UserPrincipal principal,
            Guid workstationId,
            DateTime? start = null,
            DateTime? end = null,
            string? resolution = null,
            List<string>? metrics = null,
            string? cursor = null,
            int limit = 100,
            CancellationToken cancellationToken = default);

        Task<Result<MonitoringPagedResult<MonitoringEventDto>>> GetMonitoringEventsAsync(
            UserPrincipal principal,
            Guid? workstationId = null,
            Guid? siteId = null,
            Guid? organizationId = null,
            string? eventType = null,
            DateTime? start = null,
            DateTime? end = null,
            int page = 1,
            int pageSize = 50,
            CancellationToken cancellationToken = default);

        Task<Result<MonitoringPagedResult<IncidentSummaryDto>>> GetIncidentsAsync(
            UserPrincipal principal,
            Guid? workstationId = null,
            string? pcId = null,
            Guid? siteId = null,
            Guid? organizationId = null,
            AlertSeverity? severity = null,
            IncidentLifecycleState? state = null,
            string? rule = null,
            bool? activeOnly = null,
            int page = 1,
            int pageSize = 50,
            CancellationToken cancellationToken = default);

        Task<Result<IncidentDetailDto>> GetIncidentDetailAsync(
            UserPrincipal principal,
            Guid incidentId,
            CancellationToken cancellationToken = default);

        Task<Result<OfflineOperationalSummaryDto>> GetOfflineOperationalSummaryAsync(
            UserPrincipal principal,
            Guid? siteId = null,
            Guid? organizationId = null,
            CancellationToken cancellationToken = default);
    }
}
