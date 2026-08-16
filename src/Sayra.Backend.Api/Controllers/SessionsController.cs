using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Sessions;
using Sayra.Backend.Contracts;

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
        private readonly IQueryHandler<GetSessionQuery, SessionResponseDto> _getSessionHandler;
        private readonly IQueryHandler<GetActiveSessionByWorkstationQuery, SessionResponseDto?> _getActiveByWorkstationHandler;
        private readonly IQueryHandler<GetActiveSessionByGamerQuery, SessionResponseDto?> _getActiveByGamerHandler;

        public SessionsController(
            ICommandHandler<StartSessionCommand, SessionResponseDto> startSessionHandler,
            ICommandHandler<PauseSessionCommand, SessionResponseDto> pauseSessionHandler,
            ICommandHandler<ResumeSessionCommand, SessionResponseDto> resumeSessionHandler,
            ICommandHandler<StopSessionCommand, SessionResponseDto> stopSessionHandler,
            ICommandHandler<CancelSessionCommand, SessionResponseDto> cancelSessionHandler,
            ICommandHandler<TerminateSessionCommand, SessionResponseDto> terminateSessionHandler,
            IQueryHandler<GetSessionQuery, SessionResponseDto> getSessionHandler,
            IQueryHandler<GetActiveSessionByWorkstationQuery, SessionResponseDto?> getActiveByWorkstationHandler,
            IQueryHandler<GetActiveSessionByGamerQuery, SessionResponseDto?> getActiveByGamerHandler)
        {
            _startSessionHandler = startSessionHandler ?? throw new ArgumentNullException(nameof(startSessionHandler));
            _pauseSessionHandler = pauseSessionHandler ?? throw new ArgumentNullException(nameof(pauseSessionHandler));
            _resumeSessionHandler = resumeSessionHandler ?? throw new ArgumentNullException(nameof(resumeSessionHandler));
            _stopSessionHandler = stopSessionHandler ?? throw new ArgumentNullException(nameof(stopSessionHandler));
            _cancelSessionHandler = cancelSessionHandler ?? throw new ArgumentNullException(nameof(cancelSessionHandler));
            _terminateSessionHandler = terminateSessionHandler ?? throw new ArgumentNullException(nameof(terminateSessionHandler));
            _getSessionHandler = getSessionHandler ?? throw new ArgumentNullException(nameof(getSessionHandler));
            _getActiveByWorkstationHandler = getActiveByWorkstationHandler ?? throw new ArgumentNullException(nameof(getActiveByWorkstationHandler));
            _getActiveByGamerHandler = getActiveByGamerHandler ?? throw new ArgumentNullException(nameof(getActiveByGamerHandler));
        }

        [HttpPost]
        public async Task<IActionResult> StartAsync([FromBody] StartSessionRequestDto request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return BadRequest(new { code = "INVALID_PAYLOAD", message = "Request body cannot be empty." });
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
        public async Task<IActionResult> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        {
            var query = new GetSessionQuery(id);
            var result = await _getSessionHandler.HandleAsync(query, cancellationToken);

            if (!result.IsSuccess)
            {
                return NotFound(new { code = result.ErrorCode ?? "NOT_FOUND", message = result.ErrorMessage });
            }

            return Ok(result.Value!);
        }

        [HttpPost("{id:guid}/pause")]
        public async Task<IActionResult> PauseAsync(Guid id, CancellationToken cancellationToken)
        {
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
        public async Task<IActionResult> ResumeAsync(Guid id, CancellationToken cancellationToken)
        {
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
        public async Task<IActionResult> StopAsync(Guid id, CancellationToken cancellationToken)
        {
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
        public async Task<IActionResult> CancelAsync(Guid id, CancellationToken cancellationToken)
        {
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
        public async Task<IActionResult> TerminateAsync(Guid id, [FromBody] TerminateSessionRequestDto? request, CancellationToken cancellationToken)
        {
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
