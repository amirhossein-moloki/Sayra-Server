using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Sayra.Backend.Api.Security;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Security;
using Sayra.Backend.Application.Telemetry;
using Sayra.Backend.Domain.Enums;

#nullable enable

namespace Sayra.Backend.Api.Controllers
{
    [ApiController]
    [Route("api/monitoring")]
    public class MonitoringController : ControllerBase
    {
        private readonly IMonitoringQueryService _queryService;

        public MonitoringController(IMonitoringQueryService queryService)
        {
            _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        }

        [HttpGet("workstations")]
        [HasPermission(PermissionCatalog.ViewWorkstations)]
        public async Task<IActionResult> GetFleetWorkstationsAsync(
            [FromQuery] Guid? siteId,
            [FromQuery] Guid? organizationId,
            [FromQuery] WorkstationHealthState? healthState,
            [FromQuery] string? connectionState,
            [FromQuery] string? status,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            CancellationToken cancellationToken = default)
        {
            var principal = GetPrincipal();
            if (principal == null || !principal.IsAuthenticated)
            {
                return Unauthorized(new { code = "UNAUTHORIZED", message = "Authentication is required to view fleet workstations." });
            }

            var result = await _queryService.GetFleetWorkstationsAsync(
                principal,
                siteId,
                organizationId,
                healthState,
                connectionState,
                status,
                page,
                pageSize,
                cancellationToken);

            return MapResult(result);
        }

        [HttpGet("workstations/{id:guid}")]
        [HasPermission(PermissionCatalog.ViewWorkstations)]
        public async Task<IActionResult> GetWorkstationDetailAsync(
            [FromRoute] Guid id,
            CancellationToken cancellationToken = default)
        {
            var principal = GetPrincipal();
            if (principal == null || !principal.IsAuthenticated)
            {
                return Unauthorized(new { code = "UNAUTHORIZED", message = "Authentication is required to view workstation details." });
            }

            var result = await _queryService.GetWorkstationDetailAsync(principal, id, cancellationToken);
            return MapResult(result);
        }

        [HttpGet("workstations/{id:guid}/health")]
        [HasPermission(PermissionCatalog.ViewWorkstations)]
        public async Task<IActionResult> GetWorkstationHealthAsync(
            [FromRoute] Guid id,
            CancellationToken cancellationToken = default)
        {
            var principal = GetPrincipal();
            if (principal == null || !principal.IsAuthenticated)
            {
                return Unauthorized(new { code = "UNAUTHORIZED", message = "Authentication is required to view workstation health." });
            }

            var result = await _queryService.GetWorkstationHealthAsync(principal, id, cancellationToken);
            return MapResult(result);
        }

        [HttpGet("workstations/{id:guid}/metrics")]
        [HasPermission(PermissionCatalog.ViewWorkstations)]
        public async Task<IActionResult> GetWorkstationMetricsAsync(
            [FromRoute] Guid id,
            [FromQuery] DateTime? start,
            [FromQuery] DateTime? end,
            [FromQuery] string? resolution,
            [FromQuery] List<string>? metrics,
            [FromQuery] string? cursor,
            [FromQuery] int limit = 100,
            CancellationToken cancellationToken = default)
        {
            var principal = GetPrincipal();
            if (principal == null || !principal.IsAuthenticated)
            {
                return Unauthorized(new { code = "UNAUTHORIZED", message = "Authentication is required to view workstation historical metrics." });
            }

            var result = await _queryService.GetWorkstationMetricsAsync(
                principal,
                id,
                start,
                end,
                resolution,
                metrics,
                cursor,
                limit,
                cancellationToken);

            return MapResult(result);
        }

        [HttpGet("events")]
        [HasPermission(PermissionCatalog.ViewWorkstations)]
        public async Task<IActionResult> GetMonitoringEventsAsync(
            [FromQuery] Guid? workstationId,
            [FromQuery] Guid? siteId,
            [FromQuery] Guid? organizationId,
            [FromQuery] string? eventType,
            [FromQuery] DateTime? start,
            [FromQuery] DateTime? end,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            CancellationToken cancellationToken = default)
        {
            var principal = GetPrincipal();
            if (principal == null || !principal.IsAuthenticated)
            {
                return Unauthorized(new { code = "UNAUTHORIZED", message = "Authentication is required to view monitoring events." });
            }

            var result = await _queryService.GetMonitoringEventsAsync(
                principal,
                workstationId,
                siteId,
                organizationId,
                eventType,
                start,
                end,
                page,
                pageSize,
                cancellationToken);

            return MapResult(result);
        }

        [HttpGet("incidents")]
        [HasPermission(PermissionCatalog.ViewWorkstations)]
        public async Task<IActionResult> GetIncidentsAsync(
            [FromQuery] Guid? workstationId,
            [FromQuery] string? pcId,
            [FromQuery] Guid? siteId,
            [FromQuery] Guid? organizationId,
            [FromQuery] AlertSeverity? severity,
            [FromQuery] IncidentLifecycleState? state,
            [FromQuery] string? rule,
            [FromQuery] bool? activeOnly,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            CancellationToken cancellationToken = default)
        {
            var principal = GetPrincipal();
            if (principal == null || !principal.IsAuthenticated)
            {
                return Unauthorized(new { code = "UNAUTHORIZED", message = "Authentication is required to view incidents." });
            }

            var result = await _queryService.GetIncidentsAsync(
                principal,
                workstationId,
                pcId,
                siteId,
                organizationId,
                severity,
                state,
                rule,
                activeOnly,
                page,
                pageSize,
                cancellationToken);

            return MapResult(result);
        }

        [HttpGet("incidents/{id:guid}")]
        [HasPermission(PermissionCatalog.ViewWorkstations)]
        public async Task<IActionResult> GetIncidentDetailAsync(
            [FromRoute] Guid id,
            CancellationToken cancellationToken = default)
        {
            var principal = GetPrincipal();
            if (principal == null || !principal.IsAuthenticated)
            {
                return Unauthorized(new { code = "UNAUTHORIZED", message = "Authentication is required to view incident detail." });
            }

            var result = await _queryService.GetIncidentDetailAsync(principal, id, cancellationToken);
            return MapResult(result);
        }

        [HttpGet("offline/summary")]
        [HasPermission(PermissionCatalog.ViewWorkstations)]
        public async Task<IActionResult> GetOfflineOperationalSummaryAsync(
            [FromQuery] Guid? siteId,
            [FromQuery] Guid? organizationId,
            CancellationToken cancellationToken = default)
        {
            var principal = GetPrincipal();
            if (principal == null || !principal.IsAuthenticated)
            {
                return Unauthorized(new { code = "UNAUTHORIZED", message = "Authentication is required to view offline operational summary." });
            }

            var result = await _queryService.GetOfflineOperationalSummaryAsync(principal, siteId, organizationId, cancellationToken);
            return MapResult(result);
        }

        private UserPrincipal? GetPrincipal()
        {
            return HttpContext.Items["UserPrincipal"] as UserPrincipal;
        }

        private IActionResult MapResult<T>(Sayra.Backend.Shared.Result<T> result)
        {
            if (result.IsSuccess)
            {
                return Ok(result.Value);
            }

            return result.ErrorCode switch
            {
                "UNAUTHORIZED" => Unauthorized(new { code = result.ErrorCode, message = result.ErrorMessage }),
                "PERMISSION_DENIED" or "ACCOUNT_DISABLED" or "CROSS_ORGANIZATION_ACCESS_DENIED" or "CROSS_SITE_ACCESS_DENIED" or "DEVICE_IDENTITY_MISMATCH"
                    => StatusCode(403, new { code = result.ErrorCode, message = result.ErrorMessage }),
                "WORKSTATION_NOT_FOUND" or "INCIDENT_NOT_FOUND"
                    => NotFound(new { code = result.ErrorCode, message = result.ErrorMessage }),
                "INVALID_RANGE" or "INVALID_RESOLUTION" or "INVALID_ARGUMENT"
                    => BadRequest(new { code = result.ErrorCode, message = result.ErrorMessage }),
                _ => BadRequest(new { code = result.ErrorCode ?? "BAD_REQUEST", message = result.ErrorMessage ?? "An error occurred." })
            };
        }
    }
}
