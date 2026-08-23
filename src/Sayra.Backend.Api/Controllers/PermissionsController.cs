using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Sayra.Backend.Api.Security;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Security;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Api.Controllers
{
    [ApiController]
    [Route("api/permissions")]
    public class PermissionsController : ControllerBase
    {
        private readonly IQueryHandler<GetPermissionsQuery, List<Permission>> _getPermissionsHandler;
        private readonly ICommandHandler<DisablePermissionCommand, bool> _disablePermissionHandler;

        public PermissionsController(
            IQueryHandler<GetPermissionsQuery, List<Permission>> getPermissionsHandler,
            ICommandHandler<DisablePermissionCommand, bool> disablePermissionHandler)
        {
            _getPermissionsHandler = getPermissionsHandler ?? throw new ArgumentNullException(nameof(getPermissionsHandler));
            _disablePermissionHandler = disablePermissionHandler ?? throw new ArgumentNullException(nameof(disablePermissionHandler));
        }

        [HttpGet]
        [HasPermission(PermissionCatalog.ManageUsers)]
        public async Task<IActionResult> GetPermissions(CancellationToken cancellationToken)
        {
            var result = await _getPermissionsHandler.HandleAsync(new GetPermissionsQuery(), cancellationToken);
            if (!result.IsSuccess)
            {
                return MapErrorToResponse(result.ErrorCode, result.ErrorMessage);
            }

            var dtos = result.Value?.Select(MapToPermissionDto).ToList() ?? new List<PermissionResponseDto>();
            return Ok(dtos);
        }

        [HttpPost("{code}/disable")]
        [HasPermission(PermissionCatalog.ManageUsers)]
        public async Task<IActionResult> DisablePermission([FromRoute] string code, CancellationToken cancellationToken)
        {
            var principal = GetActingPrincipal();
            var command = new DisablePermissionCommand
            {
                PermissionCode = code,
                ActingPrincipal = principal
            };

            var result = await _disablePermissionHandler.HandleAsync(command, cancellationToken);
            if (!result.IsSuccess)
            {
                return MapErrorToResponse(result.ErrorCode, result.ErrorMessage);
            }

            return Ok(new { success = true });
        }

        private UserPrincipal GetActingPrincipal()
        {
            return HttpContext.Items["UserPrincipal"] as UserPrincipal ?? UserPrincipal.Anonymous;
        }

        private static PermissionResponseDto MapToPermissionDto(Permission perm)
        {
            return new PermissionResponseDto
            {
                Id = perm.Id,
                Code = perm.Code,
                Name = perm.Name,
                Category = perm.Category,
                Description = perm.Description,
                Status = perm.Status,
                CreatedAt = perm.CreatedAt
            };
        }

        private IActionResult MapErrorToResponse(string? errorCode, string? errorMessage)
        {
            var code = errorCode ?? "ERROR";
            var msg = errorMessage ?? "An error occurred.";

            switch (code)
            {
                case "PERMISSION_NOT_FOUND":
                    return NotFound(new { code = code, message = msg });
                case "INVALID_PERMISSION_STATE":
                    return BadRequest(new { code = code, message = msg });
                case "FORBIDDEN":
                case "PERMISSION_DENIED":
                    return StatusCode(403, new { code = code, message = msg });
                case "UNAUTHORIZED":
                case "AUTH_REQUIRED":
                    return Unauthorized(new { code = code, message = msg });
                default:
                    return BadRequest(new { code = code, message = msg });
            }
        }
    }
}
