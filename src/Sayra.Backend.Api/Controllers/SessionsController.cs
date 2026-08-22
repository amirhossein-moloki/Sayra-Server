using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Sayra.Backend.Api.Security;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Security;
using Sayra.Backend.Application.Sessions;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Api.Controllers
{
    [ApiController]
    [Route("api/sessions")]
    public class SessionsController : ControllerBase
    {
        private readonly ICommandHandler<StartSessionCommand, SessionResponseDto> _startSessionHandler;
        private readonly ICommandHandler<PauseSessionCommand, SessionResponseDto> _pauseSessionHandler;
        private readonly ICommandHandler<ResumeSessionCommand, SessionResponseDto> _resumeSessionHandler;
        private readonly ICommandHandler<StopSessionCommand, SessionResponseDto> _stopSessionHandler;
        private readonly ICommandHandler<CancelSessionCommand, SessionResponseDto> _cancelSessionHandler;
        private readonly ICommandHandler<TerminateSessionCommand, SessionResponseDto> _terminateSessionHandler;
        private readonly ICommandHandler<ExtendSessionCommand, SessionExtensionResponseDto> _extendSessionHandler;
        private readonly IQueryHandler<GetSessionQuery, SessionResponseDto> _getSessionHandler;
        private readonly IQueryHandler<GetSessionTimingQuery, SessionTimingResponseDto> _getTimingHandler;
        private readonly IQueryHandler<GetActiveSessionByWorkstationQuery, SessionResponseDto?> _getActiveByWorkstationHandler;
        private readonly IQueryHandler<GetActiveSessionByGamerQuery, SessionResponseDto?> _getActiveByGamerHandler;
        private readonly IAuthorizationService _authorizationService;
        private readonly IRepository<Session> _sessionRepository;

        public SessionsController(
            ICommandHandler<StartSessionCommand, SessionResponseDto> startSessionHandler,
            ICommandHandler<PauseSessionCommand, SessionResponseDto> pauseSessionHandler,
            ICommandHandler<ResumeSessionCommand, SessionResponseDto> resumeSessionHandler,
            ICommandHandler<StopSessionCommand, SessionResponseDto> stopSessionHandler,
            ICommandHandler<CancelSessionCommand, SessionResponseDto> cancelSessionHandler,
            ICommandHandler<TerminateSessionCommand, SessionResponseDto> terminateSessionHandler,
            ICommandHandler<ExtendSessionCommand, SessionExtensionResponseDto> extendSessionHandler,
            IQueryHandler<GetSessionQuery, SessionResponseDto> getSessionHandler,
            IQueryHandler<GetSessionTimingQuery, SessionTimingResponseDto> getTimingHandler,
            IQueryHandler<GetActiveSessionByWorkstationQuery, SessionResponseDto?> getActiveByWorkstationHandler,
            IQueryHandler<GetActiveSessionByGamerQuery, SessionResponseDto?> getActiveByGamerHandler,
            IAuthorizationService authorizationService,
            IRepository<Session> sessionRepository)
        {
            _startSessionHandler = startSessionHandler ?? throw new ArgumentNullException(nameof(startSessionHandler));
            _pauseSessionHandler = pauseSessionHandler ?? throw new ArgumentNullException(nameof(pauseSessionHandler));
            _resumeSessionHandler = resumeSessionHandler ?? throw new ArgumentNullException(nameof(resumeSessionHandler));
            _stopSessionHandler = stopSessionHandler ?? throw new ArgumentNullException(nameof(stopSessionHandler));
            _cancelSessionHandler = cancelSessionHandler ?? throw new ArgumentNullException(nameof(cancelSessionHandler));
            _terminateSessionHandler = terminateSessionHandler ?? throw new ArgumentNullException(nameof(terminateSessionHandler));
            _extendSessionHandler = extendSessionHandler ?? throw new ArgumentNullException(nameof(extendSessionHandler));
            _getSessionHandler = getSessionHandler ?? throw new ArgumentNullException(nameof(getSessionHandler));
            _getTimingHandler = getTimingHandler ?? throw new ArgumentNullException(nameof(getTimingHandler));
            _getActiveByWorkstationHandler = getActiveByWorkstationHandler ?? throw new ArgumentNullException(nameof(getActiveByWorkstationHandler));
            _getActiveByGamerHandler = getActiveByGamerHandler ?? throw new ArgumentNullException(nameof(getActiveByGamerHandler));
            _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
            _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
        }

        [HttpPost]
        [HasPermission(PermissionCatalog.StartSession)]
        public async Task<IActionResult> StartAsync([FromBody] StartSessionRequestDto request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return BadRequest(new { code = "INVALID_PAYLOAD", message = "Request body cannot be empty." });
            }

            var principal = HttpContext.Items["UserPrincipal"] as UserPrincipal;
            if (principal != null && principal.Roles.All(r => string.Equals(r, RoleCatalog.Gamer, StringComparison.OrdinalIgnoreCase)))
            {
                Guid principalGamerId = principal.GamerId ?? principal.UserId ?? Guid.Empty;
                if (principalGamerId != Guid.Empty && request.GamerId != principalGamerId)
                {
                    return StatusCode(403, new { code = "CROSS_GAMER_ACCESS_DENIED", message = "Cannot start session for another gamer." });
                }
            }

            var command = new StartSessionCommand(request.GamerId, request.WorkstationId, request.ReservationId);
            var result = await _startSessionHandler.HandleAsync(command, cancellationToken);

            if (!result.IsSuccess)
            {
                if (result.ErrorCode == "WORKSTATION_HAS_ACTIVE_SESSION" || result.ErrorCode == "RESERVATION_ALREADY_CONSUMED")
                {
                    return Conflict(new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                return BadRequest(new { code = result.ErrorCode ?? "START_SESSION_FAILED", message = result.ErrorMessage });
            }

            var response = result.Value!;
            return Created($"/api/sessions/{response.SessionId}", response);
        }

        [HttpGet("{id:guid}")]
        [HasPermission(PermissionCatalog.ViewSessions)]
        public async Task<IActionResult> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        {
            var session = await _sessionRepository.GetByIdAsync(id, track: false, cancellationToken: cancellationToken);
            if (session != null)
            {
                var principal = HttpContext.Items["UserPrincipal"] as UserPrincipal;
                var authResult = await _authorizationService.AuthorizeAsync(principal, PermissionCatalog.ViewSessions, session, cancellationToken);
                if (!authResult.IsAllowed)
                {
                    return StatusCode(403, new { code = authResult.ErrorCode ?? "FORBIDDEN", message = authResult.FailureReason });
                }
            }

            var query = new GetSessionQuery(id);
            var result = await _getSessionHandler.HandleAsync(query, cancellationToken);

            if (!result.IsSuccess)
            {
                return NotFound(new { code = result.ErrorCode ?? "NOT_FOUND", message = result.ErrorMessage });
            }

            return Ok(result.Value!);
        }

        [HttpPost("{id:guid}/extend")]
        [HasPermission(PermissionCatalog.ExtendSession)]
        public async Task<IActionResult> ExtendAsync(Guid id, [FromBody] ExtendSessionRequestDto request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return BadRequest(new { code = "INVALID_PAYLOAD", message = "Request body cannot be empty." });
            }

            var session = await _sessionRepository.GetByIdAsync(id, track: false, cancellationToken: cancellationToken);
            if (session != null)
            {
                var principal = HttpContext.Items["UserPrincipal"] as UserPrincipal;
                var authResult = await _authorizationService.AuthorizeAsync(principal, PermissionCatalog.ExtendSession, session, cancellationToken);
                if (!authResult.IsAllowed)
                {
                    return StatusCode(403, new { code = authResult.ErrorCode ?? "FORBIDDEN", message = authResult.FailureReason });
                }
            }

            string? idempotencyKey = request.IdempotencyKey;
            if (string.IsNullOrWhiteSpace(idempotencyKey) && Request.Headers.TryGetValue("Idempotency-Key", out var headerVal))
            {
                idempotencyKey = headerVal.ToString();
            }

            var command = new ExtendSessionCommand(id, request.AdditionalMinutes, idempotencyKey);
            var result = await _extendSessionHandler.HandleAsync(command, cancellationToken);

            if (!result.IsSuccess)
            {
                if (result.ErrorCode == "NOT_FOUND")
                {
                    return NotFound(new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                return BadRequest(new { code = result.ErrorCode ?? "EXTEND_SESSION_FAILED", message = result.ErrorMessage });
            }

            return Ok(result.Value!);
        }

        [HttpGet("{id:guid}/timing")]
        [HasPermission(PermissionCatalog.ViewSessions)]
        public async Task<IActionResult> GetTimingAsync(Guid id, CancellationToken cancellationToken)
        {
            var session = await _sessionRepository.GetByIdAsync(id, track: false, cancellationToken: cancellationToken);
            if (session != null)
            {
                var principal = HttpContext.Items["UserPrincipal"] as UserPrincipal;
                var authResult = await _authorizationService.AuthorizeAsync(principal, PermissionCatalog.ViewSessions, session, cancellationToken);
                if (!authResult.IsAllowed)
                {
                    return StatusCode(403, new { code = authResult.ErrorCode ?? "FORBIDDEN", message = authResult.FailureReason });
                }
            }

            var query = new GetSessionTimingQuery(id);
            var result = await _getTimingHandler.HandleAsync(query, cancellationToken);

            if (!result.IsSuccess)
            {
                return NotFound(new { code = result.ErrorCode ?? "NOT_FOUND", message = result.ErrorMessage });
            }

            return Ok(result.Value!);
        }

        [HttpPost("{id:guid}/pause")]
        [HasPermission(PermissionCatalog.PauseSession)]
        public async Task<IActionResult> PauseAsync(Guid id, CancellationToken cancellationToken)
        {
            var session = await _sessionRepository.GetByIdAsync(id, track: false, cancellationToken: cancellationToken);
            if (session != null)
            {
                var principal = HttpContext.Items["UserPrincipal"] as UserPrincipal;
                var authResult = await _authorizationService.AuthorizeAsync(principal, PermissionCatalog.PauseSession, session, cancellationToken);
                if (!authResult.IsAllowed)
                {
                    return StatusCode(403, new { code = authResult.ErrorCode ?? "FORBIDDEN", message = authResult.FailureReason });
                }
            }

            var command = new PauseSessionCommand(id);
            var result = await _pauseSessionHandler.HandleAsync(command, cancellationToken);

            if (!result.IsSuccess)
            {
                if (result.ErrorCode == "NOT_FOUND")
                {
                    return NotFound(new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                return BadRequest(new { code = result.ErrorCode ?? "PAUSE_SESSION_FAILED", message = result.ErrorMessage });
            }

            return Ok(result.Value!);
        }

        [HttpPost("{id:guid}/resume")]
        [HasPermission(PermissionCatalog.ResumeSession)]
        public async Task<IActionResult> ResumeAsync(Guid id, CancellationToken cancellationToken)
        {
            var session = await _sessionRepository.GetByIdAsync(id, track: false, cancellationToken: cancellationToken);
            if (session != null)
            {
                var principal = HttpContext.Items["UserPrincipal"] as UserPrincipal;
                var authResult = await _authorizationService.AuthorizeAsync(principal, PermissionCatalog.ResumeSession, session, cancellationToken);
                if (!authResult.IsAllowed)
                {
                    return StatusCode(403, new { code = authResult.ErrorCode ?? "FORBIDDEN", message = authResult.FailureReason });
                }
            }

            var command = new ResumeSessionCommand(id);
            var result = await _resumeSessionHandler.HandleAsync(command, cancellationToken);

            if (!result.IsSuccess)
            {
                if (result.ErrorCode == "NOT_FOUND")
                {
                    return NotFound(new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                return BadRequest(new { code = result.ErrorCode ?? "RESUME_SESSION_FAILED", message = result.ErrorMessage });
            }

            return Ok(result.Value!);
        }

        [HttpPost("{id:guid}/stop")]
        [HasPermission(PermissionCatalog.StopSession)]
        public async Task<IActionResult> StopAsync(Guid id, CancellationToken cancellationToken)
        {
            var session = await _sessionRepository.GetByIdAsync(id, track: false, cancellationToken: cancellationToken);
            if (session != null)
            {
                var principal = HttpContext.Items["UserPrincipal"] as UserPrincipal;
                var authResult = await _authorizationService.AuthorizeAsync(principal, PermissionCatalog.StopSession, session, cancellationToken);
                if (!authResult.IsAllowed)
                {
                    return StatusCode(403, new { code = authResult.ErrorCode ?? "FORBIDDEN", message = authResult.FailureReason });
                }
            }

            var command = new StopSessionCommand(id);
            var result = await _stopSessionHandler.HandleAsync(command, cancellationToken);

            if (!result.IsSuccess)
            {
                if (result.ErrorCode == "NOT_FOUND")
                {
                    return NotFound(new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                return BadRequest(new { code = result.ErrorCode ?? "STOP_SESSION_FAILED", message = result.ErrorMessage });
            }

            return Ok(result.Value!);
        }

        [HttpPost("{id:guid}/cancel")]
        [HasPermission(PermissionCatalog.StopSession)]
        public async Task<IActionResult> CancelAsync(Guid id, CancellationToken cancellationToken)
        {
            var session = await _sessionRepository.GetByIdAsync(id, track: false, cancellationToken: cancellationToken);
            if (session != null)
            {
                var principal = HttpContext.Items["UserPrincipal"] as UserPrincipal;
                var authResult = await _authorizationService.AuthorizeAsync(principal, PermissionCatalog.StopSession, session, cancellationToken);
                if (!authResult.IsAllowed)
                {
                    return StatusCode(403, new { code = authResult.ErrorCode ?? "FORBIDDEN", message = authResult.FailureReason });
                }
            }

            var command = new CancelSessionCommand(id);
            var result = await _cancelSessionHandler.HandleAsync(command, cancellationToken);

            if (!result.IsSuccess)
            {
                if (result.ErrorCode == "NOT_FOUND")
                {
                    return NotFound(new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                return BadRequest(new { code = result.ErrorCode ?? "CANCEL_SESSION_FAILED", message = result.ErrorMessage });
            }

            return Ok(result.Value!);
        }

        [HttpPost("{id:guid}/terminate")]
        [HasPermission(PermissionCatalog.StopSession)]
        public async Task<IActionResult> TerminateAsync(Guid id, [FromBody] TerminateSessionRequestDto? request, CancellationToken cancellationToken)
        {
            var session = await _sessionRepository.GetByIdAsync(id, track: false, cancellationToken: cancellationToken);
            if (session != null)
            {
                var principal = HttpContext.Items["UserPrincipal"] as UserPrincipal;
                var authResult = await _authorizationService.AuthorizeAsync(principal, PermissionCatalog.StopSession, session, cancellationToken);
                if (!authResult.IsAllowed)
                {
                    return StatusCode(403, new { code = authResult.ErrorCode ?? "FORBIDDEN", message = authResult.FailureReason });
                }
            }

            var command = new TerminateSessionCommand(id, request?.Reason);
            var result = await _terminateSessionHandler.HandleAsync(command, cancellationToken);

            if (!result.IsSuccess)
            {
                if (result.ErrorCode == "NOT_FOUND")
                {
                    return NotFound(new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                return BadRequest(new { code = result.ErrorCode ?? "TERMINATE_SESSION_FAILED", message = result.ErrorMessage });
            }

            return Ok(result.Value!);
        }

        [HttpGet("workstation/{workstationId:guid}/active")]
        [HasPermission(PermissionCatalog.ViewSessions)]
        public async Task<IActionResult> GetActiveByWorkstationAsync(Guid workstationId, CancellationToken cancellationToken)
        {
            var query = new GetActiveSessionByWorkstationQuery(workstationId);
            var result = await _getActiveByWorkstationHandler.HandleAsync(query, cancellationToken);

            if (!result.IsSuccess)
            {
                return BadRequest(new { code = result.ErrorCode ?? "GET_ACTIVE_FAILED", message = result.ErrorMessage });
            }

            if (result.Value == null)
            {
                return NotFound(new { code = "NO_ACTIVE_SESSION", message = $"No active session found for workstation '{workstationId}'." });
            }

            return Ok(result.Value);
        }

        [HttpGet("gamer/{gamerId:guid}/active")]
        [HasPermission(PermissionCatalog.ViewSessions)]
        public async Task<IActionResult> GetActiveByGamerAsync(Guid gamerId, CancellationToken cancellationToken)
        {
            var query = new GetActiveSessionByGamerQuery(gamerId);
            var result = await _getActiveByGamerHandler.HandleAsync(query, cancellationToken);

            if (!result.IsSuccess)
            {
                return BadRequest(new { code = result.ErrorCode ?? "GET_ACTIVE_FAILED", message = result.ErrorMessage });
            }

            if (result.Value == null)
            {
                return NotFound(new { code = "NO_ACTIVE_SESSION", message = $"No active session found for gamer '{gamerId}'." });
            }

            return Ok(result.Value);
        }
    }
}
