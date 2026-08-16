using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Events;
using Sayra.Backend.Domain.Exceptions;
using Sayra.Backend.Shared;

namespace Sayra.Backend.Application.Reservations
{
    public class CreateReservationCommandHandler : ICommandHandler<CreateReservationCommand, ReservationResponseDto>
    {
        private readonly IRepository<Reservation> _reservationRepository;
        private readonly IRepository<Site> _siteRepository;
        private readonly IRepository<AuditEvent> _auditEventRepository;
        private readonly IReservationValidationService _validationService;
        private readonly IUnitOfWork _unitOfWork;

        public CreateReservationCommandHandler(
            IRepository<Reservation> reservationRepository,
            IRepository<Site> siteRepository,
            IRepository<AuditEvent> auditEventRepository,
            IReservationValidationService validationService,
            IUnitOfWork unitOfWork)
        {
            _reservationRepository = reservationRepository ?? throw new ArgumentNullException(nameof(reservationRepository));
            _siteRepository = siteRepository ?? throw new ArgumentNullException(nameof(siteRepository));
            _auditEventRepository = auditEventRepository ?? throw new ArgumentNullException(nameof(auditEventRepository));
            _validationService = validationService ?? throw new ArgumentNullException(nameof(validationService));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        }

        public async Task<Result<ReservationResponseDto>> HandleAsync(CreateReservationCommand command, CancellationToken cancellationToken = default)
        {
            try
            {
                var validator = new CreateReservationCommandValidator();
                var validationResult = await validator.ValidateAsync(command, cancellationToken);
                if (!validationResult.IsValid)
                {
                    var firstError = validationResult.Errors.First();
                    return Result<ReservationResponseDto>.Failure(firstError.ErrorCode ?? "VALIDATION_FAILED", firstError.ErrorMessage);
                }

                var entityValidation = await _validationService.ValidateNewReservationEntitiesAsync(
                    command.GamerId,
                    command.SiteId,
                    command.WorkstationId,
                    command.ZoneId,
                    command.StartTimeUtc,
                    command.EndTimeUtc,
                    cancellationToken);

                if (!entityValidation.IsSuccess)
                {
                    return Result<ReservationResponseDto>.Failure(entityValidation.ErrorCode ?? "VALIDATION_FAILED", entityValidation.ErrorMessage);
                }

                var site = await _siteRepository.GetByIdAsync(command.SiteId, track: false, cancellationToken);
                if (site == null)
                {
                    return Result<ReservationResponseDto>.Failure("SITE_NOT_FOUND", $"Site with ID '{command.SiteId}' not found.");
                }

                var reservation = new Reservation
                {
                    OrganizationId = site.OrganizationId,
                    SiteId = command.SiteId,
                    GamerId = command.GamerId,
                    WorkstationId = command.WorkstationId,
                    ZoneId = command.ZoneId,
                    StartTimeUtc = command.StartTimeUtc,
                    EndTimeUtc = command.EndTimeUtc,
                    Status = "PENDING",
                    ReservedAmount = command.ReservedAmount ?? 0m
                };

                reservation.NormalizeAndValidate();

                await _reservationRepository.AddAsync(reservation, cancellationToken);

                var auditEvent = new AuditEvent
                {
                    EventId = Guid.NewGuid(),
                    EventType = nameof(ReservationCreated),
                    EventVersion = 1,
                    Timestamp = DateTime.UtcNow,
                    Payload = JsonSerializer.Serialize(new ReservationCreated(
                        reservation.Id,
                        reservation.GamerId,
                        reservation.OrganizationId,
                        reservation.SiteId,
                        reservation.WorkstationId,
                        reservation.ZoneId,
                        reservation.StartTimeUtc,
                        reservation.EndTimeUtc,
                        reservation.Status,
                        reservation.ReservedAmount,
                        DateTime.UtcNow
                    ))
                };
                await _auditEventRepository.AddAsync(auditEvent, cancellationToken);

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return Result<ReservationResponseDto>.Success(MapToDto(reservation));
            }
            catch (InvalidDomainException domainEx)
            {
                return Result<ReservationResponseDto>.Failure(domainEx.ErrorCode, domainEx.Message);
            }
            catch (Exception ex)
            {
                return Result<ReservationResponseDto>.Failure("CREATE_RESERVATION_FAILED", ex.Message);
            }
        }

        private static ReservationResponseDto MapToDto(Reservation r)
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

    public class ConfirmReservationCommandHandler : ICommandHandler<ConfirmReservationCommand, ReservationResponseDto>
    {
        private readonly IRepository<Reservation> _reservationRepository;
        private readonly IRepository<AuditEvent> _auditEventRepository;
        private readonly IUnitOfWork _unitOfWork;

        public ConfirmReservationCommandHandler(
            IRepository<Reservation> reservationRepository,
            IRepository<AuditEvent> auditEventRepository,
            IUnitOfWork unitOfWork)
        {
            _reservationRepository = reservationRepository ?? throw new ArgumentNullException(nameof(reservationRepository));
            _auditEventRepository = auditEventRepository ?? throw new ArgumentNullException(nameof(auditEventRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        }

        public async Task<Result<ReservationResponseDto>> HandleAsync(ConfirmReservationCommand command, CancellationToken cancellationToken = default)
        {
            try
            {
                var validator = new ConfirmReservationCommandValidator();
                var validationResult = await validator.ValidateAsync(command, cancellationToken);
                if (!validationResult.IsValid)
                {
                    var firstError = validationResult.Errors.First();
                    return Result<ReservationResponseDto>.Failure(firstError.ErrorCode ?? "VALIDATION_FAILED", firstError.ErrorMessage);
                }

                var reservation = await _reservationRepository.GetByIdAsync(command.ReservationId, track: true, cancellationToken);
                if (reservation == null)
                {
                    return Result<ReservationResponseDto>.Failure("NOT_FOUND", $"Reservation with ID '{command.ReservationId}' not found.");
                }

                reservation.TransitionTo("CONFIRMED");
                _reservationRepository.Update(reservation);

                var auditEvent = new AuditEvent
                {
                    EventId = Guid.NewGuid(),
                    EventType = nameof(ReservationConfirmed),
                    EventVersion = 1,
                    Timestamp = DateTime.UtcNow,
                    Payload = JsonSerializer.Serialize(new ReservationConfirmed(
                        reservation.Id,
                        reservation.GamerId,
                        reservation.SiteId,
                        reservation.Status,
                        DateTime.UtcNow
                    ))
                };
                await _auditEventRepository.AddAsync(auditEvent, cancellationToken);

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return Result<ReservationResponseDto>.Success(MapToDto(reservation));
            }
            catch (InvalidDomainException domainEx)
            {
                return Result<ReservationResponseDto>.Failure(domainEx.ErrorCode, domainEx.Message);
            }
            catch (Exception ex)
            {
                return Result<ReservationResponseDto>.Failure("CONFIRM_RESERVATION_FAILED", ex.Message);
            }
        }

        private static ReservationResponseDto MapToDto(Reservation r)
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

    public class CancelReservationCommandHandler : ICommandHandler<CancelReservationCommand, ReservationResponseDto>
    {
        private readonly IRepository<Reservation> _reservationRepository;
        private readonly IRepository<AuditEvent> _auditEventRepository;
        private readonly IUnitOfWork _unitOfWork;

        public CancelReservationCommandHandler(
            IRepository<Reservation> reservationRepository,
            IRepository<AuditEvent> auditEventRepository,
            IUnitOfWork unitOfWork)
        {
            _reservationRepository = reservationRepository ?? throw new ArgumentNullException(nameof(reservationRepository));
            _auditEventRepository = auditEventRepository ?? throw new ArgumentNullException(nameof(auditEventRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        }

        public async Task<Result<ReservationResponseDto>> HandleAsync(CancelReservationCommand command, CancellationToken cancellationToken = default)
        {
            try
            {
                var validator = new CancelReservationCommandValidator();
                var validationResult = await validator.ValidateAsync(command, cancellationToken);
                if (!validationResult.IsValid)
                {
                    var firstError = validationResult.Errors.First();
                    return Result<ReservationResponseDto>.Failure(firstError.ErrorCode ?? "VALIDATION_FAILED", firstError.ErrorMessage);
                }

                var reservation = await _reservationRepository.GetByIdAsync(command.ReservationId, track: true, cancellationToken);
                if (reservation == null)
                {
                    return Result<ReservationResponseDto>.Failure("NOT_FOUND", $"Reservation with ID '{command.ReservationId}' not found.");
                }

                reservation.TransitionTo("CANCELLED");
                _reservationRepository.Update(reservation);

                var auditEvent = new AuditEvent
                {
                    EventId = Guid.NewGuid(),
                    EventType = nameof(ReservationCancelled),
                    EventVersion = 1,
                    Timestamp = DateTime.UtcNow,
                    Payload = JsonSerializer.Serialize(new ReservationCancelled(
                        reservation.Id,
                        reservation.GamerId,
                        reservation.SiteId,
                        reservation.Status,
                        command.Reason ?? "Cancelled by user",
                        DateTime.UtcNow
                    ))
                };
                await _auditEventRepository.AddAsync(auditEvent, cancellationToken);

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return Result<ReservationResponseDto>.Success(MapToDto(reservation));
            }
            catch (InvalidDomainException domainEx)
            {
                return Result<ReservationResponseDto>.Failure(domainEx.ErrorCode, domainEx.Message);
            }
            catch (Exception ex)
            {
                return Result<ReservationResponseDto>.Failure("CANCEL_RESERVATION_FAILED", ex.Message);
            }
        }

        private static ReservationResponseDto MapToDto(Reservation r)
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

    public class ActivateReservationCommandHandler : ICommandHandler<ActivateReservationCommand, ReservationResponseDto>
    {
        private readonly IRepository<Reservation> _reservationRepository;
        private readonly IRepository<AuditEvent> _auditEventRepository;
        private readonly IUnitOfWork _unitOfWork;

        public ActivateReservationCommandHandler(
            IRepository<Reservation> reservationRepository,
            IRepository<AuditEvent> auditEventRepository,
            IUnitOfWork unitOfWork)
        {
            _reservationRepository = reservationRepository ?? throw new ArgumentNullException(nameof(reservationRepository));
            _auditEventRepository = auditEventRepository ?? throw new ArgumentNullException(nameof(auditEventRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        }

        public async Task<Result<ReservationResponseDto>> HandleAsync(ActivateReservationCommand command, CancellationToken cancellationToken = default)
        {
            try
            {
                var validator = new ActivateReservationCommandValidator();
                var validationResult = await validator.ValidateAsync(command, cancellationToken);
                if (!validationResult.IsValid)
                {
                    var firstError = validationResult.Errors.First();
                    return Result<ReservationResponseDto>.Failure(firstError.ErrorCode ?? "VALIDATION_FAILED", firstError.ErrorMessage);
                }

                var reservation = await _reservationRepository.GetByIdAsync(command.ReservationId, track: true, cancellationToken);
                if (reservation == null)
                {
                    return Result<ReservationResponseDto>.Failure("NOT_FOUND", $"Reservation with ID '{command.ReservationId}' not found.");
                }

                reservation.TransitionTo("ACTIVE");
                _reservationRepository.Update(reservation);

                var auditEvent = new AuditEvent
                {
                    EventId = Guid.NewGuid(),
                    EventType = nameof(ReservationActivated),
                    EventVersion = 1,
                    Timestamp = DateTime.UtcNow,
                    Payload = JsonSerializer.Serialize(new ReservationActivated(
                        reservation.Id,
                        reservation.GamerId,
                        reservation.SiteId,
                        reservation.WorkstationId,
                        reservation.Status,
                        DateTime.UtcNow
                    ))
                };
                await _auditEventRepository.AddAsync(auditEvent, cancellationToken);

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return Result<ReservationResponseDto>.Success(MapToDto(reservation));
            }
            catch (InvalidDomainException domainEx)
            {
                return Result<ReservationResponseDto>.Failure(domainEx.ErrorCode, domainEx.Message);
            }
            catch (Exception ex)
            {
                return Result<ReservationResponseDto>.Failure("ACTIVATE_RESERVATION_FAILED", ex.Message);
            }
        }

        private static ReservationResponseDto MapToDto(Reservation r)
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

    public class GetReservationQueryHandler : IQueryHandler<GetReservationQuery, ReservationResponseDto>
    {
        private readonly IRepository<Reservation> _reservationRepository;

        public GetReservationQueryHandler(IRepository<Reservation> reservationRepository)
        {
            _reservationRepository = reservationRepository ?? throw new ArgumentNullException(nameof(reservationRepository));
        }

        public async Task<Result<ReservationResponseDto>> HandleAsync(GetReservationQuery query, CancellationToken cancellationToken = default)
        {
            var validator = new GetReservationQueryValidator();
            var validationResult = await validator.ValidateAsync(query, cancellationToken);
            if (!validationResult.IsValid)
            {
                var firstError = validationResult.Errors.First();
                return Result<ReservationResponseDto>.Failure(firstError.ErrorCode ?? "VALIDATION_FAILED", firstError.ErrorMessage);
            }

            var reservation = await _reservationRepository.GetByIdAsync(query.ReservationId, track: false, cancellationToken);
            if (reservation == null)
            {
                return Result<ReservationResponseDto>.Failure("NOT_FOUND", $"Reservation with ID '{query.ReservationId}' not found.");
            }

            return Result<ReservationResponseDto>.Success(MapToDto(reservation));
        }

        private static ReservationResponseDto MapToDto(Reservation r)
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

    public class ValidateReservationQueryHandler : IQueryHandler<ValidateReservationQuery, ReservationValidationResultDto>
    {
        private readonly IReservationValidationService _validationService;

        public ValidateReservationQueryHandler(IReservationValidationService validationService)
        {
            _validationService = validationService ?? throw new ArgumentNullException(nameof(validationService));
        }

        public async Task<Result<ReservationValidationResultDto>> HandleAsync(ValidateReservationQuery query, CancellationToken cancellationToken = default)
        {
            var validator = new ValidateReservationQueryValidator();
            var validationResult = await validator.ValidateAsync(query, cancellationToken);
            if (!validationResult.IsValid)
            {
                var firstError = validationResult.Errors.First();
                return Result<ReservationValidationResultDto>.Failure(firstError.ErrorCode ?? "VALIDATION_FAILED", firstError.ErrorMessage);
            }

            var result = await _validationService.ValidateReservationAsync(
                query.ReservationId,
                query.GamerId,
                query.SiteId,
                query.WorkstationId,
                query.CheckTimeUtc,
                cancellationToken);

            return Result<ReservationValidationResultDto>.Success(result);
        }
    }
}
