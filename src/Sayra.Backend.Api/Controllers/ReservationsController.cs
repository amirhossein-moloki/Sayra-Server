using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Sayra.Backend.Api.Security;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Reservations;
using Sayra.Backend.Application.Security;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;

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
        private readonly IAuthorizationService _authorizationService;
        private readonly IRepository<Reservation> _reservationRepository;

        public ReservationsController(
            ICommandHandler<CreateReservationCommand, ReservationResponseDto> createReservationHandler,
            ICommandHandler<ConfirmReservationCommand, ReservationResponseDto> confirmReservationHandler,
            ICommandHandler<CancelReservationCommand, ReservationResponseDto> cancelReservationHandler,
            ICommandHandler<ActivateReservationCommand, ReservationResponseDto> activateReservationHandler,
            IQueryHandler<GetReservationQuery, ReservationResponseDto> getReservationHandler,
            IQueryHandler<ValidateReservationQuery, ReservationValidationResultDto> validateReservationHandler,
            IAuthorizationService authorizationService,
            IRepository<Reservation> reservationRepository)
        {
            _createReservationHandler = createReservationHandler ?? throw new ArgumentNullException(nameof(createReservationHandler));
            _confirmReservationHandler = confirmReservationHandler ?? throw new ArgumentNullException(nameof(confirmReservationHandler));
            _cancelReservationHandler = cancelReservationHandler ?? throw new ArgumentNullException(nameof(cancelReservationHandler));
            _activateReservationHandler = activateReservationHandler ?? throw new ArgumentNullException(nameof(activateReservationHandler));
            _getReservationHandler = getReservationHandler ?? throw new ArgumentNullException(nameof(getReservationHandler));
            _validateReservationHandler = validateReservationHandler ?? throw new ArgumentNullException(nameof(validateReservationHandler));
            _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
            _reservationRepository = reservationRepository ?? throw new ArgumentNullException(nameof(reservationRepository));
        }

        [HttpPost]
        [HasPermission(PermissionCatalog.CreateReservation)]
        public async Task<IActionResult> CreateAsync([FromBody] CreateReservationRequestDto request, CancellationToken cancellationToken)
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
                    return StatusCode(403, new { code = "CROSS_GAMER_ACCESS_DENIED", message = "Cannot create reservations for another gamer." });
                }
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
        [HasPermission(PermissionCatalog.ViewReservations)]
        public async Task<IActionResult> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        {
            var reservation = await _reservationRepository.GetByIdAsync(id, track: false, cancellationToken: cancellationToken);
            if (reservation != null)
            {
                var principal = HttpContext.Items["UserPrincipal"] as UserPrincipal;
                var authResult = await _authorizationService.AuthorizeAsync(principal, PermissionCatalog.ViewReservations, reservation, cancellationToken);
                if (!authResult.IsAllowed)
                {
                    return StatusCode(403, new { code = authResult.ErrorCode ?? "FORBIDDEN", message = authResult.FailureReason });
                }
            }

            var query = new GetReservationQuery { ReservationId = id };
            var result = await _getReservationHandler.HandleAsync(query, cancellationToken);

            if (!result.IsSuccess)
            {
                return NotFound(new { code = result.ErrorCode ?? "NOT_FOUND", message = result.ErrorMessage });
            }

            return Ok(result.Value!);
        }

        [HttpPost("{id:guid}/confirm")]
        [HasPermission(PermissionCatalog.ManageReservations)]
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
        [HasPermission(PermissionCatalog.CancelReservation)]
        public async Task<IActionResult> CancelAsync(Guid id, [FromBody] CancelReservationRequestDto? request, CancellationToken cancellationToken)
        {
            var reservation = await _reservationRepository.GetByIdAsync(id, track: false, cancellationToken: cancellationToken);
            if (reservation != null)
            {
                var principal = HttpContext.Items["UserPrincipal"] as UserPrincipal;
                var authResult = await _authorizationService.AuthorizeAsync(principal, PermissionCatalog.CancelReservation, reservation, cancellationToken);
                if (!authResult.IsAllowed)
                {
                    return StatusCode(403, new { code = authResult.ErrorCode ?? "FORBIDDEN", message = authResult.FailureReason });
                }
            }

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
        [HasPermission(PermissionCatalog.ManageReservations)]
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
}
