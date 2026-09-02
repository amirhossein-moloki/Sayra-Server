using System;
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
    [Route("api/config/publications")]
    public class ConfigurationLifecycleController : ControllerBase
    {
        private readonly ICommandHandler<PreparePublicationCommand, ConfigurationPublicationDto> _prepareHandler;
        private readonly ICommandHandler<PublishConfigurationCommand, ConfigurationPublicationDto> _publishHandler;
        private readonly ICommandHandler<ActivateConfigurationCommand, ConfigurationPublicationDto> _activateHandler;
        private readonly ICommandHandler<RevokeConfigurationCommand, ConfigurationPublicationDto> _revokeHandler;
        private readonly ICommandHandler<RollbackConfigurationCommand, ConfigurationPublicationDto> _rollbackHandler;
        private readonly IQueryHandler<GetPublicationByIdQuery, ConfigurationPublicationDto?> _getByIdHandler;
        private readonly IQueryHandler<GetActiveTargetPublicationQuery, ConfigurationPublicationDto?> _getActiveTargetHandler;

        public ConfigurationLifecycleController(
            ICommandHandler<PreparePublicationCommand, ConfigurationPublicationDto> prepareHandler,
            ICommandHandler<PublishConfigurationCommand, ConfigurationPublicationDto> publishHandler,
            ICommandHandler<ActivateConfigurationCommand, ConfigurationPublicationDto> activateHandler,
            ICommandHandler<RevokeConfigurationCommand, ConfigurationPublicationDto> revokeHandler,
            ICommandHandler<RollbackConfigurationCommand, ConfigurationPublicationDto> rollbackHandler,
            IQueryHandler<GetPublicationByIdQuery, ConfigurationPublicationDto?> getByIdHandler,
            IQueryHandler<GetActiveTargetPublicationQuery, ConfigurationPublicationDto?> getActiveTargetHandler)
        {
            _prepareHandler = prepareHandler ?? throw new ArgumentNullException(nameof(prepareHandler));
            _publishHandler = publishHandler ?? throw new ArgumentNullException(nameof(publishHandler));
            _activateHandler = activateHandler ?? throw new ArgumentNullException(nameof(activateHandler));
            _revokeHandler = revokeHandler ?? throw new ArgumentNullException(nameof(revokeHandler));
            _rollbackHandler = rollbackHandler ?? throw new ArgumentNullException(nameof(rollbackHandler));
            _getByIdHandler = getByIdHandler ?? throw new ArgumentNullException(nameof(getByIdHandler));
            _getActiveTargetHandler = getActiveTargetHandler ?? throw new ArgumentNullException(nameof(getActiveTargetHandler));
        }

        [HttpPost("prepare")]
        [HasPermission(PermissionCatalog.ManageWorkstations)]
        public async Task<IActionResult> PreparePublicationAsync([FromBody] PreparePublicationCommand command, CancellationToken cancellationToken)
        {
            var result = await _prepareHandler.HandleAsync(command, cancellationToken);
            if (!result.IsSuccess)
            {
                if (result.ErrorCode == "ConfigurationVersionNotFound" || result.ErrorCode == "TargetNotFound")
                {
                    return NotFound(new { code = result.ErrorCode, message = result.ErrorMessage });
                }
                return BadRequest(new { code = result.ErrorCode ?? "PREPARE_PUBLICATION_FAILED", message = result.ErrorMessage });
            }
            return Ok(result.Value);
        }

        [HttpPost("publish")]
        [HasPermission(PermissionCatalog.ManageWorkstations)]
        public async Task<IActionResult> PublishConfigurationAsync([FromBody] PublishConfigurationCommand command, CancellationToken cancellationToken)
        {
            var result = await _publishHandler.HandleAsync(command, cancellationToken);
            if (!result.IsSuccess)
            {
                if (result.ErrorCode == "ConfigurationVersionNotFound" || result.ErrorCode == "TargetNotFound")
                {
                    return NotFound(new { code = result.ErrorCode, message = result.ErrorMessage });
                }
                return BadRequest(new { code = result.ErrorCode ?? "PUBLISH_FAILED", message = result.ErrorMessage });
            }
            return Ok(result.Value);
        }

        [HttpPost("{publicationId:guid}/activate")]
        [HasPermission(PermissionCatalog.ManageWorkstations)]
        public async Task<IActionResult> ActivateConfigurationAsync(Guid publicationId, CancellationToken cancellationToken)
        {
            var command = new ActivateConfigurationCommand(publicationId);
            var result = await _activateHandler.HandleAsync(command, cancellationToken);
            if (!result.IsSuccess)
            {
                if (result.ErrorCode == "ConfigurationNotFound")
                {
                    return NotFound(new { code = result.ErrorCode, message = result.ErrorMessage });
                }
                return BadRequest(new { code = result.ErrorCode ?? "ACTIVATION_FAILED", message = result.ErrorMessage });
            }
            return Ok(result.Value);
        }

        [HttpPost("{publicationId:guid}/revoke")]
        [HasPermission(PermissionCatalog.ManageWorkstations)]
        public async Task<IActionResult> RevokeConfigurationAsync(Guid publicationId, [FromBody] RevokeRequestDto? request, CancellationToken cancellationToken)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Reason))
            {
                return BadRequest(new { code = "REVOCATION_REASON_REQUIRED", message = "Revocation request body and Reason are required." });
            }

            var command = new RevokeConfigurationCommand(publicationId, request.Reason, request.Actor);
            var result = await _revokeHandler.HandleAsync(command, cancellationToken);
            if (!result.IsSuccess)
            {
                if (result.ErrorCode == "ConfigurationNotFound")
                {
                    return NotFound(new { code = result.ErrorCode, message = result.ErrorMessage });
                }
                return BadRequest(new { code = result.ErrorCode ?? "REVOCATION_FAILED", message = result.ErrorMessage });
            }
            return Ok(result.Value);
        }

        [HttpPost("rollback")]
        [HasPermission(PermissionCatalog.ManageWorkstations)]
        public async Task<IActionResult> RollbackConfigurationAsync([FromBody] RollbackConfigurationCommand command, CancellationToken cancellationToken)
        {
            var result = await _rollbackHandler.HandleAsync(command, cancellationToken);
            if (!result.IsSuccess)
            {
                if (result.ErrorCode == "RollbackTargetInvalid" || result.ErrorCode == "RollbackVersionInvalid")
                {
                    return NotFound(new { code = result.ErrorCode, message = result.ErrorMessage });
                }
                return BadRequest(new { code = result.ErrorCode ?? "ROLLBACK_FAILED", message = result.ErrorMessage });
            }
            return Ok(result.Value);
        }

        [HttpGet("{publicationId:guid}")]
        [HasPermission(PermissionCatalog.ViewWorkstations)]
        public async Task<IActionResult> GetPublicationByIdAsync(Guid publicationId, CancellationToken cancellationToken)
        {
            var query = new GetPublicationByIdQuery(publicationId);
            var result = await _getByIdHandler.HandleAsync(query, cancellationToken);
            if (!result.IsSuccess || result.Value == null)
            {
                return NotFound(new { code = "PUBLICATION_NOT_FOUND", message = $"Publication '{publicationId}' not found." });
            }
            return Ok(result.Value);
        }

        [HttpGet("targets/{targetId:guid}/active")]
        [HasPermission(PermissionCatalog.ViewWorkstations)]
        public async Task<IActionResult> GetActivePublicationForTargetAsync(Guid targetId, CancellationToken cancellationToken)
        {
            var query = new GetActiveTargetPublicationQuery(targetId);
            var result = await _getActiveTargetHandler.HandleAsync(query, cancellationToken);
            if (!result.IsSuccess || result.Value == null)
            {
                return NotFound(new { code = "NO_ACTIVE_PUBLICATION", message = $"No active publication found for target '{targetId}'." });
            }
            return Ok(result.Value);
        }
    }

    public class RevokeRequestDto
    {
        public string Reason { get; set; } = string.Empty;
        public string? Actor { get; set; }
    }
}
