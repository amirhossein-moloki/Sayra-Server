using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Sayra.Backend.Api.Security;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Security;
using Sayra.Backend.Application.Updates;

#nullable enable

namespace Sayra.Backend.Api.Controllers
{
    [ApiController]
    [Route("api/updates/operations")]
    public class UpdateOperationsController : ControllerBase
    {
        private readonly IQueryHandler<GetUpdateOperationalStatusQuery, UpdateOperationalStatusDto> _statusQueryHandler;

        public UpdateOperationsController(
            IQueryHandler<GetUpdateOperationalStatusQuery, UpdateOperationalStatusDto> statusQueryHandler)
        {
            _statusQueryHandler = statusQueryHandler ?? throw new ArgumentNullException(nameof(statusQueryHandler));
        }

        [HttpGet("status")]
        [HasPermission(PermissionCatalog.ViewUpdates)]
        public async Task<IActionResult> GetOperationalStatusAsync(
            [FromQuery] Guid? organizationId,
            CancellationToken cancellationToken)
        {
            var principal = HttpContext.Items["UserPrincipal"] as UserPrincipal;
            if (principal == null || !principal.IsAuthenticated)
            {
                return Unauthorized(new { code = "UNAUTHORIZED", message = "Authentication is required to view update operational status." });
            }

            var query = new GetUpdateOperationalStatusQuery
            {
                OrganizationId = organizationId ?? principal.OrganizationId ?? Guid.Empty,
                Principal = principal
            };

            var result = await _statusQueryHandler.HandleAsync(query, cancellationToken);

            if (!result.IsSuccess)
            {
                if (result.ErrorCode == "PERMISSION_DENIED")
                {
                    return StatusCode(403, new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                if (result.ErrorCode == "CROSS_ORGANIZATION_ACCESS_DENIED")
                {
                    return StatusCode(403, new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                return BadRequest(new { code = result.ErrorCode, message = result.ErrorMessage });
            }

            return Ok(result.Value);
        }
    }
}
