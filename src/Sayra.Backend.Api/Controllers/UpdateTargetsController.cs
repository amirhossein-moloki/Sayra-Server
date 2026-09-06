using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sayra.Backend.Api.Security;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Security;
using Sayra.Backend.Application.Updates;
using Sayra.Backend.Domain;

#nullable enable

namespace Sayra.Backend.Api.Controllers
{
    [ApiController]
    [Route("api/updates/targets")]
    public class UpdateTargetsController : ControllerBase
    {
        private readonly ICommandHandler<CreateUpdateTargetCommand, UpdateTargetContract> _createHandler;
        private readonly ICommandHandler<UpdateRolloutPercentageCommand, UpdateTargetContract> _updateRolloutHandler;
        private readonly ICommandHandler<DisableUpdateTargetCommand, UpdateTargetContract> _disableHandler;
        private readonly ICommandHandler<DeleteUpdateTargetCommand, bool> _deleteHandler;
        private readonly IQueryHandler<GetUpdateTargetsByReleaseQuery, IReadOnlyList<UpdateTargetContract>> _getByReleaseHandler;
        private readonly IQueryHandler<GetUpdateTargetsByOrganizationQuery, IReadOnlyList<UpdateTargetContract>> _getByOrgHandler;
        private readonly IQueryHandler<EvaluateWorkstationUpdateEligibilityQuery, UpdateEligibilityResult> _evaluateEligibilityHandler;

        public UpdateTargetsController(
            ICommandHandler<CreateUpdateTargetCommand, UpdateTargetContract> createHandler,
            ICommandHandler<UpdateRolloutPercentageCommand, UpdateTargetContract> updateRolloutHandler,
            ICommandHandler<DisableUpdateTargetCommand, UpdateTargetContract> disableHandler,
            ICommandHandler<DeleteUpdateTargetCommand, bool> deleteHandler,
            IQueryHandler<GetUpdateTargetsByReleaseQuery, IReadOnlyList<UpdateTargetContract>> getByReleaseHandler,
            IQueryHandler<GetUpdateTargetsByOrganizationQuery, IReadOnlyList<UpdateTargetContract>> getByOrgHandler,
            IQueryHandler<EvaluateWorkstationUpdateEligibilityQuery, UpdateEligibilityResult> evaluateEligibilityHandler)
        {
            _createHandler = createHandler ?? throw new ArgumentNullException(nameof(createHandler));
            _updateRolloutHandler = updateRolloutHandler ?? throw new ArgumentNullException(nameof(updateRolloutHandler));
            _disableHandler = disableHandler ?? throw new ArgumentNullException(nameof(disableHandler));
            _deleteHandler = deleteHandler ?? throw new ArgumentNullException(nameof(deleteHandler));
            _getByReleaseHandler = getByReleaseHandler ?? throw new ArgumentNullException(nameof(getByReleaseHandler));
            _getByOrgHandler = getByOrgHandler ?? throw new ArgumentNullException(nameof(getByOrgHandler));
            _evaluateEligibilityHandler = evaluateEligibilityHandler ?? throw new ArgumentNullException(nameof(evaluateEligibilityHandler));
        }

        [HttpPost]
        [HasPermission(PermissionCatalog.ManageUpdates)]
        public async Task<IActionResult> CreateTargetAsync(
            [FromBody] CreateUpdateTargetRequest request,
            [FromQuery] Guid? organizationId = null,
            CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                return BadRequest(new { code = "INVALID_REQUEST", message = "Request payload cannot be null." });
            }

            if (!Enum.TryParse<ConfigurationTargetType>(request.TargetType, true, out var targetType))
            {
                return BadRequest(new { code = "INVALID_TARGET_TYPE", message = $"Invalid target type '{request.TargetType}'." });
            }

            var principal = GetActingPrincipal();
            var targetOrgId = organizationId ?? principal.OrganizationId ?? Guid.Empty;

            var command = new CreateUpdateTargetCommand
            {
                OrganizationId = targetOrgId,
                ReleaseId = request.ReleaseId,
                TargetType = targetType,
                SiteId = request.SiteId,
                GroupId = request.GroupId,
                WorkstationId = request.WorkstationId,
                RolloutPercentage = request.RolloutPercentage,
                MinimumSupportedVersion = request.MinimumSupportedVersion,
                IsMandatoryOverride = request.IsMandatoryOverride,
                Principal = principal
            };

            var result = await _createHandler.HandleAsync(command, cancellationToken);
            if (!result.IsSuccess || result.Value == null)
            {
                return HandleError(result.ErrorCode, result.ErrorMessage);
            }

            return Created($"/api/updates/targets/{result.Value.TargetId}", result.Value);
        }

        [HttpPut("{targetId:guid}/rollout")]
        [HasPermission(PermissionCatalog.ManageUpdates)]
        public async Task<IActionResult> UpdateRolloutAsync(
            Guid targetId,
            [FromBody] UpdateRolloutPercentageRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                return BadRequest(new { code = "INVALID_REQUEST", message = "Request payload cannot be null." });
            }

            var principal = GetActingPrincipal();
            var command = new UpdateRolloutPercentageCommand
            {
                TargetId = targetId,
                RolloutPercentage = request.RolloutPercentage,
                Principal = principal
            };

            var result = await _updateRolloutHandler.HandleAsync(command, cancellationToken);
            if (!result.IsSuccess || result.Value == null)
            {
                return HandleError(result.ErrorCode, result.ErrorMessage);
            }

            return Ok(result.Value);
        }

        [HttpPost("{targetId:guid}/disable")]
        [HasPermission(PermissionCatalog.ManageUpdates)]
        public async Task<IActionResult> DisableTargetAsync(Guid targetId, CancellationToken cancellationToken = default)
        {
            var principal = GetActingPrincipal();
            var command = new DisableUpdateTargetCommand
            {
                TargetId = targetId,
                Principal = principal
            };

            var result = await _disableHandler.HandleAsync(command, cancellationToken);
            if (!result.IsSuccess || result.Value == null)
            {
                return HandleError(result.ErrorCode, result.ErrorMessage);
            }

            return Ok(result.Value);
        }

        [HttpDelete("{targetId:guid}")]
        [HasPermission(PermissionCatalog.ManageUpdates)]
        public async Task<IActionResult> DeleteTargetAsync(Guid targetId, CancellationToken cancellationToken = default)
        {
            var principal = GetActingPrincipal();
            var command = new DeleteUpdateTargetCommand
            {
                TargetId = targetId,
                Principal = principal
            };

            var result = await _deleteHandler.HandleAsync(command, cancellationToken);
            if (!result.IsSuccess)
            {
                return HandleError(result.ErrorCode, result.ErrorMessage);
            }

            return NoContent();
        }

        [HttpGet]
        [HasPermission(PermissionCatalog.ViewUpdates)]
        public async Task<IActionResult> GetTargetsAsync(
            [FromQuery] Guid? releaseId = null,
            [FromQuery] Guid? organizationId = null,
            CancellationToken cancellationToken = default)
        {
            var principal = GetActingPrincipal();

            if (releaseId.HasValue && releaseId.Value != Guid.Empty)
            {
                var query = new GetUpdateTargetsByReleaseQuery
                {
                    ReleaseId = releaseId.Value,
                    Principal = principal
                };

                var result = await _getByReleaseHandler.HandleAsync(query, cancellationToken);
                if (!result.IsSuccess || result.Value == null)
                {
                    return HandleError(result.ErrorCode, result.ErrorMessage);
                }

                return Ok(result.Value);
            }
            else
            {
                var targetOrgId = organizationId ?? principal.OrganizationId ?? Guid.Empty;
                var query = new GetUpdateTargetsByOrganizationQuery
                {
                    OrganizationId = targetOrgId,
                    Principal = principal
                };

                var result = await _getByOrgHandler.HandleAsync(query, cancellationToken);
                if (!result.IsSuccess || result.Value == null)
                {
                    return HandleError(result.ErrorCode, result.ErrorMessage);
                }

                return Ok(result.Value);
            }
        }

        [HttpPost("eligibility/evaluate")]
        [HasPermission(PermissionCatalog.ViewUpdates)]
        public async Task<IActionResult> EvaluateEligibilityAsync(
            [FromBody] EvaluateEligibilityRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request == null || request.WorkstationId == Guid.Empty)
            {
                return BadRequest(new { code = "INVALID_REQUEST", message = "Workstation ID is required for eligibility evaluation." });
            }

            var principal = GetActingPrincipal();
            var query = new EvaluateWorkstationUpdateEligibilityQuery
            {
                WorkstationId = request.WorkstationId,
                ReportedVersion = request.ReportedVersion,
                OsVersion = request.OsVersion,
                Architecture = request.Architecture,
                Principal = principal
            };

            var result = await _evaluateEligibilityHandler.HandleAsync(query, cancellationToken);
            if (!result.IsSuccess || result.Value == null)
            {
                return HandleError(result.ErrorCode, result.ErrorMessage);
            }

            return Ok(result.Value);
        }

        private IActionResult HandleError(string? errorCode, string? errorMessage)
        {
            if (errorCode == "PERMISSION_DENIED" || errorCode == "CROSS_ORGANIZATION_ACCESS_DENIED")
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { code = errorCode, message = errorMessage });
            }

            if (errorCode == "TARGET_NOT_FOUND" || errorCode == "RELEASE_NOT_FOUND" || errorCode == "WORKSTATION_NOT_FOUND")
            {
                return NotFound(new { code = errorCode, message = errorMessage });
            }

            return BadRequest(new { code = errorCode ?? "OPERATION_FAILED", message = errorMessage });
        }

        private UserPrincipal GetActingPrincipal()
        {
            return HttpContext.Items["UserPrincipal"] as UserPrincipal ?? UserPrincipal.Anonymous;
        }
    }
}
