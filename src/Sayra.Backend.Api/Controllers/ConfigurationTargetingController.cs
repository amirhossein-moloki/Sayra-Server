using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Sayra.Backend.Api.Security;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Configuration;
using Sayra.Backend.Application.Security;

namespace Sayra.Backend.Api.Controllers
{
    [ApiController]
    [Route("api/config")]
    public class ConfigurationTargetingController : ControllerBase
    {
        private readonly ICommandHandler<CreateWorkstationGroupCommand, WorkstationGroupDto> _createGroupHandler;
        private readonly ICommandHandler<AddWorkstationToGroupCommand, bool> _addWorkstationToGroupHandler;
        private readonly ICommandHandler<RemoveWorkstationFromGroupCommand, bool> _removeWorkstationFromGroupHandler;
        private readonly ICommandHandler<CreateConfigurationTargetCommand, ConfigurationTargetDto> _createTargetHandler;
        private readonly ICommandHandler<AssignConfigurationToTargetCommand, ConfigurationAssignmentDto> _assignHandler;
        private readonly ICommandHandler<UnassignConfigurationFromTargetCommand, bool> _unassignHandler;
        private readonly IQueryHandler<GetConfigurationAssignmentsQuery, List<ConfigurationAssignmentDto>> _getAssignmentsHandler;
        private readonly IQueryHandler<GetApplicableAssignmentsForWorkstationQuery, List<ApplicableAssignmentDto>> _getApplicableAssignmentsHandler;
        private readonly IQueryHandler<ResolveEffectiveConfigurationQuery, Application.Configuration.Models.ConfigurationResolutionResult> _resolveEffectiveHandler;

        public ConfigurationTargetingController(
            ICommandHandler<CreateWorkstationGroupCommand, WorkstationGroupDto> createGroupHandler,
            ICommandHandler<AddWorkstationToGroupCommand, bool> addWorkstationToGroupHandler,
            ICommandHandler<RemoveWorkstationFromGroupCommand, bool> removeWorkstationFromGroupHandler,
            ICommandHandler<CreateConfigurationTargetCommand, ConfigurationTargetDto> createTargetHandler,
            ICommandHandler<AssignConfigurationToTargetCommand, ConfigurationAssignmentDto> assignHandler,
            ICommandHandler<UnassignConfigurationFromTargetCommand, bool> unassignHandler,
            IQueryHandler<GetConfigurationAssignmentsQuery, List<ConfigurationAssignmentDto>> getAssignmentsHandler,
            IQueryHandler<GetApplicableAssignmentsForWorkstationQuery, List<ApplicableAssignmentDto>> getApplicableAssignmentsHandler,
            IQueryHandler<ResolveEffectiveConfigurationQuery, Application.Configuration.Models.ConfigurationResolutionResult> resolveEffectiveHandler)
        {
            _createGroupHandler = createGroupHandler ?? throw new ArgumentNullException(nameof(createGroupHandler));
            _addWorkstationToGroupHandler = addWorkstationToGroupHandler ?? throw new ArgumentNullException(nameof(addWorkstationToGroupHandler));
            _removeWorkstationFromGroupHandler = removeWorkstationFromGroupHandler ?? throw new ArgumentNullException(nameof(removeWorkstationFromGroupHandler));
            _createTargetHandler = createTargetHandler ?? throw new ArgumentNullException(nameof(createTargetHandler));
            _assignHandler = assignHandler ?? throw new ArgumentNullException(nameof(assignHandler));
            _unassignHandler = unassignHandler ?? throw new ArgumentNullException(nameof(unassignHandler));
            _getAssignmentsHandler = getAssignmentsHandler ?? throw new ArgumentNullException(nameof(getAssignmentsHandler));
            _getApplicableAssignmentsHandler = getApplicableAssignmentsHandler ?? throw new ArgumentNullException(nameof(getApplicableAssignmentsHandler));
            _resolveEffectiveHandler = resolveEffectiveHandler ?? throw new ArgumentNullException(nameof(resolveEffectiveHandler));
        }

        [HttpPost("groups")]
        [HasPermission(PermissionCatalog.ManageWorkstations)]
        public async Task<IActionResult> CreateGroupAsync([FromBody] CreateWorkstationGroupCommand command, CancellationToken cancellationToken)
        {
            var result = await _createGroupHandler.HandleAsync(command, cancellationToken);
            if (!result.IsSuccess)
            {
                if (result.ErrorCode == "ORGANIZATION_NOT_FOUND" || result.ErrorCode == "SITE_NOT_FOUND")
                {
                    return NotFound(new { code = result.ErrorCode, message = result.ErrorMessage });
                }
                return BadRequest(new { code = result.ErrorCode ?? "CREATE_GROUP_FAILED", message = result.ErrorMessage });
            }
            return Ok(result.Value);
        }

        [HttpPost("groups/{groupId:guid}/workstations/{workstationId:guid}")]
        [HasPermission(PermissionCatalog.ManageWorkstations)]
        public async Task<IActionResult> AddWorkstationToGroupAsync(Guid groupId, Guid workstationId, CancellationToken cancellationToken)
        {
            var command = new AddWorkstationToGroupCommand { GroupId = groupId, WorkstationId = workstationId };
            var result = await _addWorkstationToGroupHandler.HandleAsync(command, cancellationToken);
            if (!result.IsSuccess)
            {
                if (result.ErrorCode == "GROUP_NOT_FOUND" || result.ErrorCode == "WORKSTATION_NOT_FOUND")
                {
                    return NotFound(new { code = result.ErrorCode, message = result.ErrorMessage });
                }
                return BadRequest(new { code = result.ErrorCode ?? "ADD_MEMBER_FAILED", message = result.ErrorMessage });
            }
            return Ok(new { success = true });
        }

        [HttpDelete("groups/{groupId:guid}/workstations/{workstationId:guid}")]
        [HasPermission(PermissionCatalog.ManageWorkstations)]
        public async Task<IActionResult> RemoveWorkstationFromGroupAsync(Guid groupId, Guid workstationId, CancellationToken cancellationToken)
        {
            var command = new RemoveWorkstationFromGroupCommand { GroupId = groupId, WorkstationId = workstationId };
            var result = await _removeWorkstationFromGroupHandler.HandleAsync(command, cancellationToken);
            if (!result.IsSuccess)
            {
                return BadRequest(new { code = result.ErrorCode ?? "REMOVE_MEMBER_FAILED", message = result.ErrorMessage });
            }
            return Ok(new { success = true });
        }

        [HttpPost("targets")]
        [HasPermission(PermissionCatalog.ManageWorkstations)]
        public async Task<IActionResult> CreateTargetAsync([FromBody] CreateConfigurationTargetCommand command, CancellationToken cancellationToken)
        {
            var result = await _createTargetHandler.HandleAsync(command, cancellationToken);
            if (!result.IsSuccess)
            {
                if (result.ErrorCode == "ORGANIZATION_NOT_FOUND" || result.ErrorCode == "SITE_NOT_FOUND" ||
                    result.ErrorCode == "GROUP_NOT_FOUND" || result.ErrorCode == "WORKSTATION_NOT_FOUND")
                {
                    return NotFound(new { code = result.ErrorCode, message = result.ErrorMessage });
                }
                return BadRequest(new { code = result.ErrorCode ?? "CREATE_TARGET_FAILED", message = result.ErrorMessage });
            }
            return Ok(result.Value);
        }

        [HttpPost("assignments")]
        [HasPermission(PermissionCatalog.ManageWorkstations)]
        public async Task<IActionResult> AssignConfigurationAsync([FromBody] AssignConfigurationToTargetCommand command, CancellationToken cancellationToken)
        {
            var result = await _assignHandler.HandleAsync(command, cancellationToken);
            if (!result.IsSuccess)
            {
                if (result.ErrorCode == "PACKAGE_NOT_FOUND" || result.ErrorCode == "TARGET_NOT_FOUND")
                {
                    return NotFound(new { code = result.ErrorCode, message = result.ErrorMessage });
                }
                if (result.ErrorCode == "DUPLICATE_ASSIGNMENT")
                {
                    return Conflict(new { code = result.ErrorCode, message = result.ErrorMessage });
                }
                return BadRequest(new { code = result.ErrorCode ?? "ASSIGNMENT_FAILED", message = result.ErrorMessage });
            }
            return Ok(result.Value);
        }

        [HttpDelete("assignments/{id:guid}")]
        [HasPermission(PermissionCatalog.ManageWorkstations)]
        public async Task<IActionResult> UnassignConfigurationAsync(Guid id, CancellationToken cancellationToken)
        {
            var command = new UnassignConfigurationFromTargetCommand { ConfigurationAssignmentId = id };
            var result = await _unassignHandler.HandleAsync(command, cancellationToken);
            if (!result.IsSuccess)
            {
                if (result.ErrorCode == "ASSIGNMENT_NOT_FOUND")
                {
                    return NotFound(new { code = result.ErrorCode, message = result.ErrorMessage });
                }
                return BadRequest(new { code = result.ErrorCode ?? "UNASSIGNMENT_FAILED", message = result.ErrorMessage });
            }
            return Ok(new { success = true });
        }

        [HttpGet("assignments")]
        [HasPermission(PermissionCatalog.ViewWorkstations)]
        public async Task<IActionResult> GetAssignmentsAsync([FromQuery] Guid? targetId, [FromQuery] Guid? packageId, CancellationToken cancellationToken)
        {
            var query = new GetConfigurationAssignmentsQuery { TargetId = targetId, PackageId = packageId };
            var result = await _getAssignmentsHandler.HandleAsync(query, cancellationToken);
            if (!result.IsSuccess)
            {
                return BadRequest(new { code = result.ErrorCode ?? "GET_ASSIGNMENTS_FAILED", message = result.ErrorMessage });
            }
            return Ok(result.Value);
        }

        [HttpGet("workstations/{workstationId:guid}/applicable-assignments")]
        [HasPermission(PermissionCatalog.ViewWorkstations)]
        public async Task<IActionResult> GetApplicableAssignmentsForWorkstationAsync(Guid workstationId, CancellationToken cancellationToken)
        {
            var query = new GetApplicableAssignmentsForWorkstationQuery { WorkstationId = workstationId };
            var result = await _getApplicableAssignmentsHandler.HandleAsync(query, cancellationToken);
            if (!result.IsSuccess)
            {
                if (result.ErrorCode == "WORKSTATION_NOT_FOUND")
                {
                    return NotFound(new { code = result.ErrorCode, message = result.ErrorMessage });
                }
                return BadRequest(new { code = result.ErrorCode ?? "GET_APPLICABLE_ASSIGNMENTS_FAILED", message = result.ErrorMessage });
            }
            return Ok(result.Value);
        }

        [HttpGet("workstations/{workstationId:guid}/effective")]
        [HasPermission(PermissionCatalog.ViewWorkstations)]
        public async Task<IActionResult> GetEffectiveConfigurationAsync(Guid workstationId, CancellationToken cancellationToken)
        {
            var query = new ResolveEffectiveConfigurationQuery { WorkstationId = workstationId };
            var result = await _resolveEffectiveHandler.HandleAsync(query, cancellationToken);
            if (!result.IsSuccess)
            {
                if (result.ErrorCode == "WORKSTATION_NOT_FOUND" || result.ErrorCode == "SITE_NOT_FOUND" || result.ErrorCode == "ORGANIZATION_NOT_FOUND")
                {
                    return NotFound(new { code = result.ErrorCode, message = result.ErrorMessage });
                }
                return BadRequest(new { code = result.ErrorCode ?? "RESOLUTION_FAILED", message = result.ErrorMessage });
            }
            return Ok(result.Value);
        }
    }
}
