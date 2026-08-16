using System;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Domain;
using Sayra.Backend.Shared;

namespace Sayra.Backend.Application.Sessions
{
    public class SessionStateTransitionService : ISessionStateTransitionService
    {
        private readonly IRepository<Gamer> _gamerRepository;
        private readonly IRepository<Workstation> _workstationRepository;
        private readonly IRepository<Reservation> _reservationRepository;
        private readonly IRepository<Session> _sessionRepository;

        public SessionStateTransitionService(
            IRepository<Gamer> gamerRepository,
            IRepository<Workstation> workstationRepository,
            IRepository<Reservation> reservationRepository,
            IRepository<Session> sessionRepository)
        {
            _gamerRepository = gamerRepository ?? throw new ArgumentNullException(nameof(gamerRepository));
            _workstationRepository = workstationRepository ?? throw new ArgumentNullException(nameof(workstationRepository));
            _reservationRepository = reservationRepository ?? throw new ArgumentNullException(nameof(reservationRepository));
            _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
        }

        public async Task<Result<bool>> ValidateNewSessionAsync(
            Guid gamerId,
            Guid workstationId,
            Guid? reservationId,
            CancellationToken cancellationToken = default)
        {
            // 1. Validate Gamer
            if (gamerId == Guid.Empty)
            {
                return Result<bool>.Failure("INVALID_GAMER_ID", "GamerId cannot be empty.");
            }

            var gamer = await _gamerRepository.GetByIdAsync(gamerId, track: false, cancellationToken);
            if (gamer == null)
            {
                return Result<bool>.Failure("GAMER_NOT_FOUND", $"Gamer with ID '{gamerId}' not found.");
            }

            if (!gamer.CanOperate())
            {
                return Result<bool>.Failure("GAMER_DISABLED", $"Gamer '{gamer.Username}' is disabled or not active.");
            }

            // 2. Validate Workstation
            if (workstationId == Guid.Empty)
            {
                return Result<bool>.Failure("INVALID_WORKSTATION_ID", "WorkstationId cannot be empty.");
            }

            var workstation = await _workstationRepository.GetByIdAsync(workstationId, track: false, cancellationToken);
            if (workstation == null)
            {
                return Result<bool>.Failure("WORKSTATION_NOT_FOUND", $"Workstation with ID '{workstationId}' not found.");
            }

            if (workstation.IsDeactivated || workstation.IsDisabled)
            {
                return Result<bool>.Failure("WORKSTATION_DISABLED", $"Workstation '{workstation.PcId}' is disabled or deactivated.");
            }

            if (workstation.SiteEntityId == null || workstation.SiteEntityId == Guid.Empty)
            {
                return Result<bool>.Failure("WORKSTATION_UNASSIGNED", $"Workstation '{workstation.PcId}' is not assigned to any Site.");
            }

            // 3. Validate Workstation Active Session Uniqueness
            var activeSession = await _sessionRepository.FirstOrDefaultAsync(
                s => s.WorkstationId == workstationId &&
                     (s.Status == "STARTING" || s.Status == "ACTIVE" || s.Status == "PAUSED" || s.Status == "ENDING"),
                track: false,
                cancellationToken);

            if (activeSession != null)
            {
                return Result<bool>.Failure("WORKSTATION_HAS_ACTIVE_SESSION", $"Workstation '{workstation.PcId}' already has an active session '{activeSession.Id}'.");
            }

            // 4. Validate Reservation if provided
            if (reservationId.HasValue && reservationId.Value != Guid.Empty)
            {
                var reservation = await _reservationRepository.GetByIdAsync(reservationId.Value, track: false, cancellationToken);
                if (reservation == null)
                {
                    return Result<bool>.Failure("RESERVATION_NOT_FOUND", $"Reservation with ID '{reservationId.Value}' not found.");
                }

                if (reservation.GamerId != gamerId)
                {
                    return Result<bool>.Failure("RESERVATION_GAMER_MISMATCH", $"Reservation '{reservationId.Value}' does not belong to Gamer '{gamerId}'.");
                }

                if (reservation.SiteId != workstation.SiteEntityId.Value)
                {
                    return Result<bool>.Failure("RESERVATION_SITE_MISMATCH", $"Reservation '{reservationId.Value}' Site does not match Workstation Site.");
                }

                if (!reservation.IsActiveOrConfirmed())
                {
                    return Result<bool>.Failure("RESERVATION_INVALID_STATUS", $"Reservation status '{reservation.Status}' is not valid for starting a session.");
                }

                var reservationActiveSession = await _sessionRepository.FirstOrDefaultAsync(
                    s => s.ReservationId == reservationId.Value &&
                         (s.Status == "STARTING" || s.Status == "ACTIVE" || s.Status == "PAUSED" || s.Status == "ENDING"),
                    track: false,
                    cancellationToken);

                if (reservationActiveSession != null)
                {
                    return Result<bool>.Failure("RESERVATION_ALREADY_CONSUMED", $"Reservation '{reservationId.Value}' is already attached to active session '{reservationActiveSession.Id}'.");
                }
            }

            return Result<bool>.Success(true);
        }
    }
}
