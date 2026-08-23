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
    [Route("api")]
    public class RolesController : ControllerBase
    {
        private readonly ICommandHandler<CreateRoleCommand, Role> _createRoleHandler;
        private readonly ICommandHandler<AssignRoleToUserCommand, bool> _assignRoleHandler;
        private readonly ICommandHandler<RemoveRoleFromUserCommand, bool> _removeRoleHandler;
        private readonly ICommandHandler<AssignPermissionToRoleCommand, bool> _assignPermissionHandler;
        private readonly ICommandHandler<RemovePermissionFromRoleCommand, bool> _removePermissionHandler;
        private readonly ICommandHandler<DisableRoleCommand, bool> _disableRoleHandler;
        private readonly IQueryHandler<GetRolesQuery, List<Role>> _getRolesHandler;
        private readonly IQueryHandler<GetUserRolesQuery, List<Role>> _getUserRolesHandler;
        private readonly IQueryHandler<GetRolePermissionsQuery, List<Permission>> _getRolePermissionsHandler;

        public RolesController(
            ICommandHandler<CreateRoleCommand, Role> createRoleHandler,
            ICommandHandler<AssignRoleToUserCommand, bool> assignRoleHandler,
            ICommandHandler<RemoveRoleFromUserCommand, bool> removeRoleHandler,
            ICommandHandler<AssignPermissionToRoleCommand, bool> assignPermissionHandler,
            ICommandHandler<RemovePermissionFromRoleCommand, bool> removePermissionHandler,
            ICommandHandler<DisableRoleCommand, bool> disableRoleHandler,
            IQueryHandler<GetRolesQuery, List<Role>> getRolesHandler,
            IQueryHandler<GetUserRolesQuery, List<Role>> getUserRolesHandler,
            IQueryHandler<GetRolePermissionsQuery, List<Permission>> getRolePermissionsHandler)
        {
            _createRoleHandler = createRoleHandler ?? throw new ArgumentNullException(nameof(createRoleHandler));
            _assignRoleHandler = assignRoleHandler ?? throw new ArgumentNullException(nameof(assignRoleHandler));
            _removeRoleHandler = removeRoleHandler ?? throw new ArgumentNullException(nameof(removeRoleHandler));
            _assignPermissionHandler = assignPermissionHandler ?? throw new ArgumentNullException(nameof(assignPermissionHandler));
            _removePermissionHandler = removePermissionHandler ?? throw new ArgumentNullException(nameof(removePermissionHandler));
            _disableRoleHandler = disableRoleHandler ?? throw new ArgumentNullException(nameof(disableRoleHandler));
            _getRolesHandler = getRolesHandler ?? throw new ArgumentNullException(nameof(getRolesHandler));
            _getUserRolesHandler = getUserRolesHandler ?? throw new ArgumentNullException(nameof(getUserRolesHandler));
            _getRolePermissionsHandler = getRolePermissionsHandler ?? throw new ArgumentNullException(nameof(getRolePermissionsHandler));
        }

        [HttpGet("roles")]
        [HasPermission(PermissionCatalog.ManageUsers)]
        public async Task<IActionResult> GetRoles(CancellationToken cancellationToken)
        {
            var result = await _getRolesHandler.HandleAsync(new GetRolesQuery(), cancellationToken);
            if (!result.IsSuccess)
            {
                return MapErrorToResponse(result.ErrorCode, result.ErrorMessage);
            }

            var dtos = result.Value?.Select(MapToRoleDto).ToList() ?? new List<RoleResponseDto>();
            return Ok(dtos);
        }

        [HttpPost("roles")]
        [HasPermission(PermissionCatalog.ManageUsers)]
        public async Task<IActionResult> CreateRole([FromBody] CreateRoleRequestDto request, CancellationToken cancellationToken)
        {
            var principal = GetActingPrincipal();
            var command = new CreateRoleCommand
            {
                Code = request.Code,
                Name = request.Name,
                Description = request.Description,
                ActingPrincipal = principal
            };

            var result = await _createRoleHandler.HandleAsync(command, cancellationToken);
            if (!result.IsSuccess)
            {
                return MapErrorToResponse(result.ErrorCode, result.ErrorMessage);
            }

            var dto = MapToRoleDto(result.Value!);
            return CreatedAtAction(nameof(GetRoles), new { code = dto.Code }, dto);
        }

        [HttpGet("users/{id:guid}/roles")]
        [HasPermission(PermissionCatalog.ManageUsers)]
        public async Task<IActionResult> GetUserRoles([FromRoute] Guid id, CancellationToken cancellationToken)
        {
            var result = await _getUserRolesHandler.HandleAsync(new GetUserRolesQuery { UserEntityId = id }, cancellationToken);
            if (!result.IsSuccess)
            {
                return MapErrorToResponse(result.ErrorCode, result.ErrorMessage);
            }

            var dtos = result.Value?.Select(MapToRoleDto).ToList() ?? new List<RoleResponseDto>();
            return Ok(dtos);
        }

        [HttpPost("users/{id:guid}/roles")]
        [HasPermission(PermissionCatalog.ManageUsers)]
        public async Task<IActionResult> AssignRoleToUser([FromRoute] Guid id, [FromBody] AssignRoleRequestDto request, CancellationToken cancellationToken)
        {
            var principal = GetActingPrincipal();
            var command = new AssignRoleToUserCommand
            {
                UserEntityId = id,
                RoleCode = request.RoleCode,
                ActingPrincipal = principal
            };

            var result = await _assignRoleHandler.HandleAsync(command, cancellationToken);
            if (!result.IsSuccess)
            {
                return MapErrorToResponse(result.ErrorCode, result.ErrorMessage);
            }

            return Ok(new { success = true });
        }

        [HttpDelete("users/{id:guid}/roles/{roleCode}")]
        [HasPermission(PermissionCatalog.ManageUsers)]
        public async Task<IActionResult> RemoveRoleFromUser([FromRoute] Guid id, [FromRoute] string roleCode, CancellationToken cancellationToken)
        {
            var principal = GetActingPrincipal();
            var command = new RemoveRoleFromUserCommand
            {
                UserEntityId = id,
                RoleCode = roleCode,
                ActingPrincipal = principal
            };

            var result = await _removeRoleHandler.HandleAsync(command, cancellationToken);
            if (!result.IsSuccess)
            {
                return MapErrorToResponse(result.ErrorCode, result.ErrorMessage);
            }

            return Ok(new { success = true });
        }

        [HttpGet("roles/{code}/permissions")]
        [HasPermission(PermissionCatalog.ManageUsers)]
        public async Task<IActionResult> GetRolePermissions([FromRoute] string code, CancellationToken cancellationToken)
        {
            var result = await _getRolePermissionsHandler.HandleAsync(new GetRolePermissionsQuery { RoleCode = code }, cancellationToken);
            if (!result.IsSuccess)
            {
                return MapErrorToResponse(result.ErrorCode, result.ErrorMessage);
            }

            var dtos = result.Value?.Select(MapToPermissionDto).ToList() ?? new List<PermissionResponseDto>();
            return Ok(dtos);
        }

        [HttpPost("roles/{code}/permissions")]
        [HasPermission(PermissionCatalog.ManageUsers)]
        public async Task<IActionResult> AssignPermissionToRole([FromRoute] string code, [FromBody] AssignPermissionRequestDto request, CancellationToken cancellationToken)
        {
            var principal = GetActingPrincipal();
            var command = new AssignPermissionToRoleCommand
            {
                RoleCode = code,
                PermissionCode = request.PermissionCode,
                ActingPrincipal = principal
            };

            var result = await _assignPermissionHandler.HandleAsync(command, cancellationToken);
            if (!result.IsSuccess)
            {
                return MapErrorToResponse(result.ErrorCode, result.ErrorMessage);
            }

            return Ok(new { success = true });
        }

        [HttpDelete("roles/{code}/permissions/{permissionCode}")]
        [HasPermission(PermissionCatalog.ManageUsers)]
        public async Task<IActionResult> RemovePermissionFromRole([FromRoute] string code, [FromRoute] string permissionCode, CancellationToken cancellationToken)
        {
            var principal = GetActingPrincipal();
            var command = new RemovePermissionFromRoleCommand
            {
                RoleCode = code,
                PermissionCode = permissionCode,
                ActingPrincipal = principal
            };

            var result = await _removePermissionHandler.HandleAsync(command, cancellationToken);
            if (!result.IsSuccess)
            {
                return MapErrorToResponse(result.ErrorCode, result.ErrorMessage);
            }

            return Ok(new { success = true });
        }

        [HttpPost("roles/{code}/disable")]
        [HasPermission(PermissionCatalog.ManageUsers)]
        public async Task<IActionResult> DisableRole([FromRoute] string code, CancellationToken cancellationToken)
        {
            var principal = GetActingPrincipal();
            var command = new DisableRoleCommand
            {
                RoleCode = code,
                ActingPrincipal = principal
            };

            var result = await _disableRoleHandler.HandleAsync(command, cancellationToken);
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

        private static RoleResponseDto MapToRoleDto(Role role)
        {
            return new RoleResponseDto
            {
                Id = role.Id,
                Code = role.Code,
                Name = role.Name,
                Description = role.Description,
                Status = role.Status,
                IsSystemRole = role.IsSystemRole,
                CreatedAt = role.CreatedAt
            };
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
                case "ROLE_NOT_FOUND":
                case "PERMISSION_NOT_FOUND":
                case "USER_NOT_FOUND":
                    return NotFound(new { code = code, message = msg });
                case "ROLE_ALREADY_ASSIGNED":
                case "PERMISSION_ALREADY_ASSIGNED":
                case "ROLE_ALREADY_EXISTS":
                    return Conflict(new { code = code, message = msg });
                case "INVALID_ROLE_STATE":
                case "INVALID_PERMISSION_STATE":
                case "INVALID_ROLE_CODE":
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
