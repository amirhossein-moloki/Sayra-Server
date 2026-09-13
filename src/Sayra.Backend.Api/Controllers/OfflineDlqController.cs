using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Sayra.Backend.Api.Security;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.OfflineQueue;
using Sayra.Backend.Application.Security;
using Sayra.Backend.Contracts;

#nullable enable

namespace Sayra.Backend.Api.Controllers
{
    [ApiController]
    [Route("api/v1/offline/dlq")]
    public class OfflineDlqController : ControllerBase
    {
        private readonly IOfflineDlqService _dlqService;

        public OfflineDlqController(IOfflineDlqService dlqService)
        {
            _dlqService = dlqService ?? throw new ArgumentNullException(nameof(dlqService));
        }

        [HttpGet]
        [HasPermission(PermissionCatalog.ViewWorkstations)]
        public async Task<IActionResult> GetDlqEventsAsync(
            [FromQuery] string? clientId,
            [FromQuery] string? siteId,
            [FromQuery] Guid? organizationId,
            [FromQuery] string? failureCode,
            [FromQuery] string? processingStatus,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            CancellationToken cancellationToken = default)
        {
            var principal = GetPrincipal();
            if (principal == null || !principal.IsAuthenticated)
            {
                return Unauthorized(new { code = "UNAUTHORIZED", message = "Authentication is required." });
            }

            var result = await _dlqService.GetDlqEventsAsync(
                principal,
                clientId,
                siteId,
                organizationId,
                failureCode,
                processingStatus,
                page,
                pageSize,
                cancellationToken);

            return MapResult(result);
        }

        [HttpGet("{eventId:guid}")]
        [HasPermission(PermissionCatalog.ViewWorkstations)]
        public async Task<IActionResult> GetDlqEventByIdAsync(
            [FromRoute] Guid eventId,
            CancellationToken cancellationToken = default)
        {
            var principal = GetPrincipal();
            if (principal == null || !principal.IsAuthenticated)
            {
                return Unauthorized(new { code = "UNAUTHORIZED", message = "Authentication is required." });
            }

            var result = await _dlqService.GetDlqEventByIdAsync(principal, eventId, cancellationToken);
            return MapResult(result);
        }

        [HttpPost("{eventId:guid}/retry")]
        [HasPermission(PermissionCatalog.ManageWorkstations)]
        public async Task<IActionResult> RetryDlqEventAsync(
            [FromRoute] Guid eventId,
            CancellationToken cancellationToken = default)
        {
            var principal = GetPrincipal();
            if (principal == null || !principal.IsAuthenticated)
            {
                return Unauthorized(new { code = "UNAUTHORIZED", message = "Authentication is required." });
            }

            var result = await _dlqService.RetryDlqEventAsync(principal, eventId, cancellationToken);
            return MapResult(result);
        }

        [HttpPost("{eventId:guid}/reject")]
        [HasPermission(PermissionCatalog.ManageWorkstations)]
        public async Task<IActionResult> RejectDlqEventAsync(
            [FromRoute] Guid eventId,
            [FromBody] RejectDlqRequestDto? request,
            CancellationToken cancellationToken = default)
        {
            var principal = GetPrincipal();
            if (principal == null || !principal.IsAuthenticated)
            {
                return Unauthorized(new { code = "UNAUTHORIZED", message = "Authentication is required." });
            }

            var result = await _dlqService.RejectDlqEventAsync(principal, eventId, request?.Reason, cancellationToken);
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
                "PERMISSION_DENIED" or "ACCOUNT_DISABLED" or "CROSS_ORGANIZATION_ACCESS_DENIED" or "CROSS_SITE_ACCESS_DENIED"
                    => StatusCode(403, new { code = result.ErrorCode, message = result.ErrorMessage }),
                "DLQ_EVENT_NOT_FOUND" => NotFound(new { code = result.ErrorCode, message = result.ErrorMessage }),
                _ => BadRequest(new { code = result.ErrorCode ?? "BAD_REQUEST", message = result.ErrorMessage ?? "An error occurred." })
            };
        }
    }

    public class RejectDlqRequestDto
    {
        public string? Reason { get; set; }
    }
}
