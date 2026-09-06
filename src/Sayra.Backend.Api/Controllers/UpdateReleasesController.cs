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
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Api.Controllers
{
    [ApiController]
    [Route("api/updates/releases")]
    public class UpdateReleasesController : ControllerBase
    {
        private readonly ICommandHandler<CreateUpdateReleaseCommand, ClientUpdateReleaseContract> _createHandler;
        private readonly ICommandHandler<UpdateReleaseMetadataCommand, ClientUpdateReleaseContract> _updateMetadataHandler;
        private readonly ICommandHandler<PrepareUpdateReleaseCommand, ClientUpdateReleaseContract> _prepareHandler;
        private readonly ICommandHandler<PublishUpdateReleaseCommand, ClientUpdateReleaseContract> _publishHandler;
        private readonly ICommandHandler<ActivateUpdateReleaseCommand, ClientUpdateReleaseContract> _activateHandler;
        private readonly ICommandHandler<RevokeUpdateReleaseCommand, ClientUpdateReleaseContract> _revokeHandler;
        private readonly ICommandHandler<RollbackUpdateReleaseCommand, ClientUpdateReleaseContract> _rollbackHandler;
        private readonly IQueryHandler<GetUpdateReleaseByIdQuery, ClientUpdateReleaseContract> _getByIdHandler;
        private readonly IQueryHandler<GetUpdateReleasesByOrganizationQuery, IReadOnlyList<ClientUpdateReleaseContract>> _getByOrgHandler;
        private readonly IQueryHandler<GetActiveUpdateReleaseQuery, ClientUpdateReleaseContract> _getActiveHandler;

        public UpdateReleasesController(
            ICommandHandler<CreateUpdateReleaseCommand, ClientUpdateReleaseContract> createHandler,
            ICommandHandler<UpdateReleaseMetadataCommand, ClientUpdateReleaseContract> updateMetadataHandler,
            ICommandHandler<PrepareUpdateReleaseCommand, ClientUpdateReleaseContract> prepareHandler,
            ICommandHandler<PublishUpdateReleaseCommand, ClientUpdateReleaseContract> publishHandler,
            ICommandHandler<ActivateUpdateReleaseCommand, ClientUpdateReleaseContract> activateHandler,
            ICommandHandler<RevokeUpdateReleaseCommand, ClientUpdateReleaseContract> revokeHandler,
            ICommandHandler<RollbackUpdateReleaseCommand, ClientUpdateReleaseContract> rollbackHandler,
            IQueryHandler<GetUpdateReleaseByIdQuery, ClientUpdateReleaseContract> getByIdHandler,
            IQueryHandler<GetUpdateReleasesByOrganizationQuery, IReadOnlyList<ClientUpdateReleaseContract>> getByOrgHandler,
            IQueryHandler<GetActiveUpdateReleaseQuery, ClientUpdateReleaseContract> getActiveHandler)
        {
            _createHandler = createHandler ?? throw new ArgumentNullException(nameof(createHandler));
            _updateMetadataHandler = updateMetadataHandler ?? throw new ArgumentNullException(nameof(updateMetadataHandler));
            _prepareHandler = prepareHandler ?? throw new ArgumentNullException(nameof(prepareHandler));
            _publishHandler = publishHandler ?? throw new ArgumentNullException(nameof(publishHandler));
            _activateHandler = activateHandler ?? throw new ArgumentNullException(nameof(activateHandler));
            _revokeHandler = revokeHandler ?? throw new ArgumentNullException(nameof(revokeHandler));
            _rollbackHandler = rollbackHandler ?? throw new ArgumentNullException(nameof(rollbackHandler));
            _getByIdHandler = getByIdHandler ?? throw new ArgumentNullException(nameof(getByIdHandler));
            _getByOrgHandler = getByOrgHandler ?? throw new ArgumentNullException(nameof(getByOrgHandler));
            _getActiveHandler = getActiveHandler ?? throw new ArgumentNullException(nameof(getActiveHandler));
        }

        [HttpPost]
        [HasPermission(PermissionCatalog.ManageUpdates)]
        public async Task<IActionResult> CreateReleaseAsync(
            [FromBody] CreateUpdateReleaseRequest request,
            [FromQuery] Guid? organizationId = null,
            CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                return BadRequest(new { code = "INVALID_REQUEST", message = "Request payload cannot be null." });
            }

            if (!Enum.TryParse<UpdateReleaseType>(request.ReleaseType, true, out var releaseType))
            {
                return BadRequest(new { code = "INVALID_RELEASE_TYPE", message = $"Invalid release type '{request.ReleaseType}'." });
            }

            var principal = GetActingPrincipal();
            var targetOrgId = organizationId ?? principal.OrganizationId ?? Guid.Empty;

            var command = new CreateUpdateReleaseCommand
            {
                OrganizationId = targetOrgId,
                Version = request.Version,
                ReleaseType = releaseType,
                ReleaseNotes = request.ReleaseNotes,
                Metadata = request.Metadata,
                Principal = principal
            };

            var result = await _createHandler.HandleAsync(command, cancellationToken);
            if (!result.IsSuccess || result.Value == null)
            {
                return HandleError(result.ErrorCode, result.ErrorMessage);
            }

            return Created($"/api/updates/releases/{result.Value.ReleaseId}", result.Value);
        }

        [HttpGet("{releaseId:guid}")]
        [HasPermission(PermissionCatalog.ViewUpdates)]
        public async Task<IActionResult> GetReleaseByIdAsync(Guid releaseId, CancellationToken cancellationToken = default)
        {
            var principal = GetActingPrincipal();
            var query = new GetUpdateReleaseByIdQuery
            {
                ReleaseId = releaseId,
                Principal = principal
            };

            var result = await _getByIdHandler.HandleAsync(query, cancellationToken);
            if (!result.IsSuccess || result.Value == null)
            {
                return HandleError(result.ErrorCode, result.ErrorMessage);
            }

            return Ok(result.Value);
        }

        [HttpGet]
        [HasPermission(PermissionCatalog.ViewUpdates)]
        public async Task<IActionResult> GetReleasesByOrganizationAsync(
            [FromQuery] Guid? organizationId = null,
            CancellationToken cancellationToken = default)
        {
            var principal = GetActingPrincipal();
            var targetOrgId = organizationId ?? principal.OrganizationId ?? Guid.Empty;

            var query = new GetUpdateReleasesByOrganizationQuery
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

        [HttpGet("active")]
        [HasPermission(PermissionCatalog.ViewUpdates)]
        public async Task<IActionResult> GetActiveReleaseAsync(
            [FromQuery] Guid? organizationId = null,
            CancellationToken cancellationToken = default)
        {
            var principal = GetActingPrincipal();
            var targetOrgId = organizationId ?? principal.OrganizationId ?? Guid.Empty;

            var query = new GetActiveUpdateReleaseQuery
            {
                OrganizationId = targetOrgId,
                Principal = principal
            };

            var result = await _getActiveHandler.HandleAsync(query, cancellationToken);
            if (!result.IsSuccess || result.Value == null)
            {
                return HandleError(result.ErrorCode, result.ErrorMessage);
            }

            return Ok(result.Value);
        }

        [HttpPut("{releaseId:guid}/metadata")]
        [HasPermission(PermissionCatalog.ManageUpdates)]
        public async Task<IActionResult> UpdateMetadataAsync(
            Guid releaseId,
            [FromBody] UpdateReleaseMetadataRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                return BadRequest(new { code = "INVALID_REQUEST", message = "Request payload cannot be null." });
            }

            var principal = GetActingPrincipal();
            var command = new UpdateReleaseMetadataCommand
            {
                ReleaseId = releaseId,
                ReleaseNotes = request.ReleaseNotes,
                Metadata = request.Metadata,
                Principal = principal
            };

            var result = await _updateMetadataHandler.HandleAsync(command, cancellationToken);
            if (!result.IsSuccess || result.Value == null)
            {
                return HandleError(result.ErrorCode, result.ErrorMessage);
            }

            return Ok(result.Value);
        }

        [HttpPost("{releaseId:guid}/prepare")]
        [HasPermission(PermissionCatalog.ManageUpdates)]
        public async Task<IActionResult> PrepareReleaseAsync(Guid releaseId, CancellationToken cancellationToken = default)
        {
            var principal = GetActingPrincipal();
            var command = new PrepareUpdateReleaseCommand
            {
                ReleaseId = releaseId,
                Principal = principal
            };

            var result = await _prepareHandler.HandleAsync(command, cancellationToken);
            if (!result.IsSuccess || result.Value == null)
            {
                return HandleError(result.ErrorCode, result.ErrorMessage);
            }

            return Ok(result.Value);
        }

        [HttpPost("{releaseId:guid}/publish")]
        [HasPermission(PermissionCatalog.ManageUpdates)]
        public async Task<IActionResult> PublishReleaseAsync(Guid releaseId, CancellationToken cancellationToken = default)
        {
            var principal = GetActingPrincipal();
            var command = new PublishUpdateReleaseCommand
            {
                ReleaseId = releaseId,
                Principal = principal
            };

            var result = await _publishHandler.HandleAsync(command, cancellationToken);
            if (!result.IsSuccess || result.Value == null)
            {
                return HandleError(result.ErrorCode, result.ErrorMessage);
            }

            return Ok(result.Value);
        }

        [HttpPost("{releaseId:guid}/activate")]
        [HasPermission(PermissionCatalog.ManageUpdates)]
        public async Task<IActionResult> ActivateReleaseAsync(Guid releaseId, CancellationToken cancellationToken = default)
        {
            var principal = GetActingPrincipal();
            var command = new ActivateUpdateReleaseCommand
            {
                ReleaseId = releaseId,
                Principal = principal
            };

            var result = await _activateHandler.HandleAsync(command, cancellationToken);
            if (!result.IsSuccess || result.Value == null)
            {
                return HandleError(result.ErrorCode, result.ErrorMessage);
            }

            return Ok(result.Value);
        }

        [HttpPost("{releaseId:guid}/revoke")]
        [HasPermission(PermissionCatalog.ManageUpdates)]
        public async Task<IActionResult> RevokeReleaseAsync(
            Guid releaseId,
            [FromBody] RevokeUpdateReleaseRequest? request = null,
            CancellationToken cancellationToken = default)
        {
            var principal = GetActingPrincipal();
            var command = new RevokeUpdateReleaseCommand
            {
                ReleaseId = releaseId,
                Reason = request?.Reason ?? "Emergency Administrative Revocation",
                Principal = principal
            };

            var result = await _revokeHandler.HandleAsync(command, cancellationToken);
            if (!result.IsSuccess || result.Value == null)
            {
                return HandleError(result.ErrorCode, result.ErrorMessage);
            }

            return Ok(result.Value);
        }

        [HttpPost("{currentReleaseId:guid}/rollback")]
        [HasPermission(PermissionCatalog.ManageUpdates)]
        public async Task<IActionResult> RollbackReleaseAsync(
            Guid currentReleaseId,
            [FromBody] RollbackUpdateReleaseRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request == null || request.TargetReleaseId == Guid.Empty)
            {
                return BadRequest(new { code = "INVALID_REQUEST", message = "Target release ID is required for rollback." });
            }

            var principal = GetActingPrincipal();
            var command = new RollbackUpdateReleaseCommand
            {
                CurrentReleaseId = currentReleaseId,
                TargetReleaseId = request.TargetReleaseId,
                Reason = request.Reason,
                Principal = principal
            };

            var result = await _rollbackHandler.HandleAsync(command, cancellationToken);
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

            if (errorCode == "RELEASE_NOT_FOUND" || errorCode == "TARGET_RELEASE_NOT_FOUND" || errorCode == "NO_ACTIVE_RELEASE")
            {
                return NotFound(new { code = errorCode, message = errorMessage });
            }

            if (errorCode == "TOCTOU_INTEGRITY_VIOLATION")
            {
                return StatusCode(StatusCodes.Status422UnprocessableEntity, new { code = errorCode, message = errorMessage });
            }

            return BadRequest(new { code = errorCode ?? "OPERATION_FAILED", message = errorMessage });
        }

        private UserPrincipal GetActingPrincipal()
        {
            return HttpContext.Items["UserPrincipal"] as UserPrincipal ?? UserPrincipal.Anonymous;
        }
    }
}
