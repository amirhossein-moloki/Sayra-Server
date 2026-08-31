using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Shared;

namespace Sayra.Backend.Application.Reservations
{
    public class ReservationValidationService : IReservationValidationService
    {
        private readonly IRepository<Reservation> _reservationRepository;
        private readonly IRepository<Gamer> _gamerRepository;
        private readonly IRepository<Site> _siteRepository;
        private readonly IRepository<Organization> _organizationRepository;
        private readonly IRepository<Workstation> _workstationRepository;
        private readonly IRepository<Zone> _zoneRepository;

        public ReservationValidationService(
            IRepository<Reservation> reservationRepository,
            IRepository<Gamer> gamerRepository,
            IRepository<Site> siteRepository,
            IRepository<Organization> organizationRepository,
            IRepository<Workstation> workstationRepository,
            IRepository<Zone> zoneRepository)
        {
            _reservationRepository = reservationRepository ?? throw new ArgumentNullException(nameof(reservationRepository));
            _gamerRepository = gamerRepository ?? throw new ArgumentNullException(nameof(gamerRepository));
            _siteRepository = siteRepository ?? throw new ArgumentNullException(nameof(siteRepository));
            _organizationRepository = organizationRepository ?? throw new ArgumentNullException(nameof(organizationRepository));
            _workstationRepository = workstationRepository ?? throw new ArgumentNullException(nameof(workstationRepository));
            _zoneRepository = zoneRepository ?? throw new ArgumentNullException(nameof(zoneRepository));
        }

        public async Task<Result> ValidateNewReservationEntitiesAsync(
            Guid gamerId,
            Guid siteId,
            Guid? workstationId,
            Guid? zoneId,
            DateTime startTimeUtc,
            DateTime endTimeUtc,
            CancellationToken cancellationToken = default)
        {
            // 1. Gamer checks
            var gamer = await _gamerRepository.GetByIdAsync(gamerId, track: false, cancellationToken);
            if (gamer == null)
            {
                return Result.Failure("GAMER_NOT_FOUND", $"Gamer with ID '{gamerId}' not found.");
            }
            if (!gamer.CanOperate())
            {
                return Result.Failure("GAMER_NOT_ELIGIBLE", $"Gamer '{gamer.GamerId}' is not active (Status: {gamer.Status}).");
            }

            // 2. Site checks
            var site = await _siteRepository.GetByIdAsync(siteId, track: false, cancellationToken);
            if (site == null)
            {
                return Result.Failure("SITE_NOT_FOUND", $"Site with ID '{siteId}' not found.");
            }
            if (!site.CanOperate())
            {
                return Result.Failure("SITE_NOT_ELIGIBLE", $"Site '{site.Code}' is not active (Status: {site.Status}).");
            }

            // 3. Organization checks
            var org = await _organizationRepository.GetByIdAsync(site.OrganizationId, track: false, cancellationToken);
            if (org == null)
            {
                return Result.Failure("ORGANIZATION_NOT_FOUND", $"Organization with ID '{site.OrganizationId}' not found.");
            }
            if (!org.CanOperate())
            {
                return Result.Failure("ORGANIZATION_NOT_ELIGIBLE", $"Organization '{org.Code}' is not active (Status: {org.Status}).");
            }

            // 4. Workstation checks
            if (workstationId.HasValue && workstationId.Value != Guid.Empty)
            {
                var ws = await _workstationRepository.GetByIdAsync(workstationId.Value, track: false, cancellationToken);
                if (ws == null)
                {
                    return Result.Failure("WORKSTATION_NOT_FOUND", $"Workstation with ID '{workstationId.Value}' not found.");
                }
                if (ws.IsDisabled || ws.IsDeactivated)
                {
                    return Result.Failure("WORKSTATION_NOT_ELIGIBLE", $"Workstation '{ws.PcId}' is disabled or deactivated.");
                }
                if (ws.SiteEntityId.HasValue && ws.SiteEntityId.Value != siteId)
                {
                    return Result.Failure("WORKSTATION_SITE_MISMATCH", $"Workstation '{ws.PcId}' does not belong to Site '{site.Code}'.");
                }

                // Workstation Overlap Validation - execute indexed database predicate search rather than full table scan via GetAllAsync()
                var existingWsReservations = await _reservationRepository.FindAsync(
                    r => r.WorkstationId == workstationId.Value,
                    track: false,
                    cancellationToken);
                var hasConflict = existingWsReservations.Any(r =>
                    r.IsActiveOrConfirmed() &&
                    r.StartTimeUtc < endTimeUtc &&
                    r.EndTimeUtc > startTimeUtc);

                if (hasConflict)
                {
                    return Result.Failure("RESERVATION_CONFLICT", $"Workstation '{ws.PcId}' already has an overlapping active reservation for the specified timeframe.");
                }
            }

            // 5. Zone checks
            if (zoneId.HasValue && zoneId.Value != Guid.Empty)
            {
                var zone = await _zoneRepository.GetByIdAsync(zoneId.Value, track: false, cancellationToken);
                if (zone == null)
                {
                    return Result.Failure("ZONE_NOT_FOUND", $"Zone with ID '{zoneId.Value}' not found.");
                }
                if (!zone.CanOperate())
                {
                    return Result.Failure("ZONE_NOT_ELIGIBLE", $"Zone '{zone.Code}' is not active (Status: {zone.Status}).");
                }
                if (zone.SiteId != siteId)
                {
                    return Result.Failure("ZONE_SITE_MISMATCH", $"Zone '{zone.Code}' does not belong to Site '{site.Code}'.");
                }
            }

            return Result.Success();
        }

        public async Task<ReservationValidationResultDto> ValidateReservationAsync(
            Guid? reservationId,
            Guid? gamerId,
            Guid? siteId,
            Guid? workstationId,
            DateTime? checkTimeUtc,
            CancellationToken cancellationToken = default)
        {
            Reservation? reservation = null;

            if (reservationId.HasValue && reservationId.Value != Guid.Empty)
            {
                reservation = await _reservationRepository.GetByIdAsync(reservationId.Value, track: false, cancellationToken);
            }
            else
            {
                // Lookup active/confirmed reservation by gamerId, siteId, workstationId - execute indexed database lookup rather than full table scan via GetAllAsync()
                var matchingReservations = await _reservationRepository.FindAsync(
                    r => (gamerId == null || r.GamerId == gamerId) &&
                         (siteId == null || r.SiteId == siteId) &&
                         (workstationId == null || r.WorkstationId == workstationId),
                    track: false,
                    cancellationToken);

                reservation = matchingReservations
                    .Where(r => r.IsActiveOrConfirmed())
                    .OrderByDescending(r => r.CreatedAt)
                    .FirstOrDefault();
            }

            if (reservation == null)
            {
                return ReservationValidationResultDto.Invalid("RESERVATION_NOT_FOUND", "No matching reservation was found.");
            }

            var dto = MapToResponseDto(reservation);

            // Gamer ownership check
            if (gamerId.HasValue && gamerId.Value != Guid.Empty && reservation.GamerId != gamerId.Value)
            {
                return ReservationValidationResultDto.Invalid("GAMER_MISMATCH", $"Reservation does not belong to Gamer '{gamerId.Value}'.", dto);
            }

            // Site check
            if (siteId.HasValue && siteId.Value != Guid.Empty && reservation.SiteId != siteId.Value)
            {
                return ReservationValidationResultDto.Invalid("SITE_MISMATCH", $"Reservation does not belong to Site '{siteId.Value}'.", dto);
            }

            // Workstation check
            if (workstationId.HasValue && workstationId.Value != Guid.Empty && reservation.WorkstationId.HasValue && reservation.WorkstationId.Value != workstationId.Value)
            {
                return ReservationValidationResultDto.Invalid("WORKSTATION_MISMATCH", $"Reservation does not belong to Workstation '{workstationId.Value}'.", dto);
            }

            // Consumption / Status state check
            if (reservation.Status == "COMPLETED" || reservation.Status == "CANCELLED" ||
                reservation.Status == "EXPIRED" || reservation.Status == "NO_SHOW")
            {
                return ReservationValidationResultDto.Invalid("RESERVATION_CONSUMED", $"Reservation is in terminal/consumed status '{reservation.Status}'.", dto);
            }

            // Check Gamer status
            var gamer = await _gamerRepository.GetByIdAsync(reservation.GamerId, track: false, cancellationToken);
            if (gamer == null || !gamer.CanOperate())
            {
                return ReservationValidationResultDto.Invalid("GAMER_NOT_ELIGIBLE", "Associated gamer is not active.", dto);
            }

            // Check Site status
            var site = await _siteRepository.GetByIdAsync(reservation.SiteId, track: false, cancellationToken);
            if (site == null || !site.CanOperate())
            {
                return ReservationValidationResultDto.Invalid("SITE_NOT_ELIGIBLE", "Associated site is not active.", dto);
            }

            // Check Workstation status if applicable
            if (reservation.WorkstationId.HasValue)
            {
                var ws = await _workstationRepository.GetByIdAsync(reservation.WorkstationId.Value, track: false, cancellationToken);
                if (ws == null || ws.IsDisabled || ws.IsDeactivated)
                {
                    return ReservationValidationResultDto.Invalid("WORKSTATION_NOT_ELIGIBLE", "Associated workstation is disabled or deactivated.", dto);
                }
            }

            // Time window validation
            var evalTime = checkTimeUtc ?? DateTime.UtcNow;
            if (evalTime.Kind == DateTimeKind.Unspecified)
            {
                evalTime = DateTime.SpecifyKind(evalTime, DateTimeKind.Utc);
            }
            else
            {
                evalTime = evalTime.ToUniversalTime();
            }

            if (evalTime > reservation.EndTimeUtc)
            {
                return ReservationValidationResultDto.Invalid("RESERVATION_EXPIRED", $"Current time ({evalTime:O}) is past reservation end time ({reservation.EndTimeUtc:O}).", dto);
            }

            return ReservationValidationResultDto.Valid(dto, "Reservation is valid and ready for session initialization.");
        }

        private static ReservationResponseDto MapToResponseDto(Reservation r)
        {
            return new ReservationResponseDto
            {
                ReservationId = r.Id,
                OrganizationId = r.OrganizationId,
                SiteId = r.SiteId,
                GamerId = r.GamerId,
                WorkstationId = r.WorkstationId,
                ZoneId = r.ZoneId,
                StartTimeUtc = r.StartTimeUtc,
                EndTimeUtc = r.EndTimeUtc,
                Status = r.Status,
                ReservedAmount = r.ReservedAmount,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt
            };
        }
    }
}
