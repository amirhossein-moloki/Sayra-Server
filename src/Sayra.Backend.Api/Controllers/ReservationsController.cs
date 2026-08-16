using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Reservations;
using Sayra.Backend.Contracts;

namespace Sayra.Backend.Api.Controllers
{
    [ApiController]
    [Route("api/reservations")]
    public class ReservationsController : ControllerBase
    {
        private readonly ICommandHandler<CreateReservationCommand, ReservationResponseDto> _createReservationHandler;
        private readonly ICommandHandler<ConfirmReservationCommand, ReservationResponseDto> _confirmReservationHandler;
        private readonly ICommandHandler<CancelReservationCommand, ReservationResponseDto> _cancelReservationHandler;
        private readonly ICommandHandler<ActivateReservationCommand, ReservationResponseDto> _activateReservationHandler;
        private readonly IQueryHandler<GetReservationQuery, ReservationResponseDto> _getReservationHandler;
        private readonly IQueryHandler<ValidateReservationQuery, ReservationValidationResultDto> _validateReservationHandler;

        public ReservationsController(
            ICommandHandler<CreateReservationCommand, ReservationResponseDto> createReservationHandler,
            ICommandHandler<ConfirmReservationCommand, ReservationResponseDto> confirmReservationHandler,
            ICommandHandler<CancelReservationCommand, ReservationResponseDto> cancelReservationHandler,
            ICommandHandler<ActivateReservationCommand, ReservationResponseDto> activateReservationHandler,
            IQueryHandler<GetReservationQuery, ReservationResponseDto> getReservationHandler,
            IQueryHandler<ValidateReservationQuery, ReservationValidationResultDto> validateReservationHandler)
        {
            _createReservationHandler = createReservationHandler ?? throw new ArgumentNullException(nameof(createReservationHandler));
            _confirmReservationHandler = confirmReservationHandler ?? throw new ArgumentNullException(nameof(confirmReservationHandler));
            _cancelReservationHandler = cancelReservationHandler ?? throw new ArgumentNullException(nameof(cancelReservationHandler));
            _activateReservationHandler = activateReservationHandler ?? throw new ArgumentNullException(nameof(activateReservationHandler));
            _getReservationHandler = getReservationHandler ?? throw new ArgumentNullException(nameof(getReservationHandler));
            _validateReservationHandler = validateReservationHandler ?? throw new ArgumentNullException(nameof(validateReservationHandler));
        }

        [HttpPost]
        public async Task<IActionResult> CreateAsync([FromBody] CreateReservationRequestDto request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return BadRequest(new { code = "INVALID_PAYLOAD", message = "Request body cannot be empty." });
            }

            var command = new CreateReservationCommand
            {
                GamerId = request.GamerId,
                SiteId = request.SiteId,
                WorkstationId = request.WorkstationId,
                ZoneId = request.ZoneId,
                StartTimeUtc = request.StartTimeUtc,
                EndTimeUtc = request.EndTimeUtc,
                ReservedAmount = request.ReservedAmount
            };

            var result = await _createReservationHandler.HandleAsync(command, cancellationToken);

            if (!result.IsSuccess)
            {
                if (result.ErrorCode == "RESERVATION_CONFLICT")
                {
                    return Conflict(new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                return BadRequest(new { code = result.ErrorCode ?? "CREATE_RESERVATION_FAILED", message = result.ErrorMessage });
            }

            var response = result.Value!;
            return Created($"/api/reservations/{response.ReservationId}", response);
        }

        [HttpGet("validate")]
        public async Task<IActionResult> ValidateAsync(
            [FromQuery] Guid? reservationId,
            [FromQuery] Guid? gamerId,
            [FromQuery] Guid? siteId,
            [FromQuery] Guid? workstationId,
            [FromQuery] DateTime? checkTimeUtc,
            CancellationToken cancellationToken)
        {
            var query = new ValidateReservationQuery
            {
                ReservationId = reservationId,
                GamerId = gamerId,
                SiteId = siteId,
                WorkstationId = workstationId,
                CheckTimeUtc = checkTimeUtc
            };

            var result = await _validateReservationHandler.HandleAsync(query, cancellationToken);

            if (!result.IsSuccess)
            {
                return BadRequest(new { code = result.ErrorCode ?? "VALIDATION_FAILED", message = result.ErrorMessage });
            }

            return Ok(result.Value!);
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        {
            var query = new GetReservationQuery { ReservationId = id };
            var result = await _getReservationHandler.HandleAsync(query, cancellationToken);

            if (!result.IsSuccess)
            {
                return NotFound(new { code = result.ErrorCode ?? "NOT_FOUND", message = result.ErrorMessage });
            }

            return Ok(result.Value!);
        }

        [HttpPost("{id:guid}/confirm")]
        public async Task<IActionResult> ConfirmAsync(Guid id, CancellationToken cancellationToken)
        {
            var command = new ConfirmReservationCommand { ReservationId = id };
            var result = await _confirmReservationHandler.HandleAsync(command, cancellationToken);

            if (!result.IsSuccess)
            {
                if (result.ErrorCode == "NOT_FOUND")
                {
                    return NotFound(new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                return BadRequest(new { code = result.ErrorCode ?? "CONFIRM_FAILED", message = result.ErrorMessage });
            }

            return Ok(result.Value!);
        }

        [HttpPost("{id:guid}/cancel")]
        public async Task<IActionResult> CancelAsync(Guid id, [FromBody] CancelReservationRequestDto? request, CancellationToken cancellationToken)
        {
            var command = new CancelReservationCommand
            {
                ReservationId = id,
                Reason = request?.Reason
            };

            var result = await _cancelReservationHandler.HandleAsync(command, cancellationToken);

            if (!result.IsSuccess)
            {
                if (result.ErrorCode == "NOT_FOUND")
                {
                    return NotFound(new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                return BadRequest(new { code = result.ErrorCode ?? "CANCEL_FAILED", message = result.ErrorMessage });
            }

            return Ok(result.Value!);
        }

        [HttpPost("{id:guid}/activate")]
        public async Task<IActionResult> ActivateAsync(Guid id, CancellationToken cancellationToken)
        {
            var command = new ActivateReservationCommand { ReservationId = id };
            var result = await _activateReservationHandler.HandleAsync(command, cancellationToken);

            if (!result.IsSuccess)
            {
                if (result.ErrorCode == "NOT_FOUND")
                {
                    return NotFound(new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                return BadRequest(new { code = result.ErrorCode ?? "ACTIVATE_FAILED", message = result.ErrorMessage });
            }

            return Ok(result.Value!);
        }
    }

    public class CancelReservationRequestDto
    {
        public string? Reason { get; set; }
    }
}
