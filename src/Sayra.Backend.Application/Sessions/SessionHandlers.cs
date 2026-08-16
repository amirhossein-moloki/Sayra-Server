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

namespace Sayra.Backend.Application.Sessions
{
    public class StartSessionCommandHandler : ICommandHandler<StartSessionCommand, SessionResponseDto>
    {
        private readonly IRepository<Session> _sessionRepository;
        private readonly IRepository<Workstation> _workstationRepository;
        private readonly IRepository<Reservation> _reservationRepository;
        private readonly IRepository<AuditEvent> _auditEventRepository;
        private readonly ISessionStateTransitionService _transitionService;
        private readonly IUnitOfWork _unitOfWork;

        public StartSessionCommandHandler(
            IRepository<Session> sessionRepository,
            IRepository<Workstation> workstationRepository,
            IRepository<Reservation> reservationRepository,
            IRepository<AuditEvent> auditEventRepository,
            ISessionStateTransitionService transitionService,
            IUnitOfWork unitOfWork)
        {
            _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
            _workstationRepository = workstationRepository ?? throw new ArgumentNullException(nameof(workstationRepository));
            _reservationRepository = reservationRepository ?? throw new ArgumentNullException(nameof(reservationRepository));
            _auditEventRepository = auditEventRepository ?? throw new ArgumentNullException(nameof(auditEventRepository));
            _transitionService = transitionService ?? throw new ArgumentNullException(nameof(transitionService));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        }

        public async Task<Result<SessionResponseDto>> HandleAsync(StartSessionCommand command, CancellationToken cancellationToken = default)
        {
            try
            {
                var validator = new StartSessionCommandValidator();
                var validationResult = await validator.ValidateAsync(command, cancellationToken);
                if (!validationResult.IsValid)
                {
                    var firstError = validationResult.Errors.First();
                    return Result<SessionResponseDto>.Failure(firstError.ErrorCode ?? "VALIDATION_FAILED", firstError.ErrorMessage);
                }

                var validation = await _transitionService.ValidateNewSessionAsync(
                    command.GamerId,
                    command.WorkstationId,
                    command.ReservationId,
                    cancellationToken);

                if (!validation.IsSuccess)
                {
                    return Result<SessionResponseDto>.Failure(validation.ErrorCode ?? "VALIDATION_FAILED", validation.ErrorMessage);
                }

                var workstation = await _workstationRepository.GetByIdAsync(command.WorkstationId, track: false, cancellationToken);
                if (workstation == null || workstation.OrganizationEntityId == null || workstation.SiteEntityId == null)
                {
                    return Result<SessionResponseDto>.Failure("WORKSTATION_INVALID", "Workstation is missing organizational hierarchy assignment.");
                }

                if (command.ReservationId.HasValue && command.ReservationId.Value != Guid.Empty)
                {
                    var reservation = await _reservationRepository.GetByIdAsync(command.ReservationId.Value, track: true, cancellationToken);
                    if (reservation != null && reservation.Status == "CONFIRMED")
                    {
                        reservation.TransitionTo("ACTIVE");
                        _reservationRepository.Update(reservation);
                    }
                }

                var session = new Session
                {
                    OrganizationId = workstation.OrganizationEntityId.Value,
                    SiteId = workstation.SiteEntityId.Value,
                    WorkstationId = command.WorkstationId,
                    GamerId = command.GamerId,
                    ReservationId = command.ReservationId,
                    StartedAt = DateTime.UtcNow,
                    Status = "IDLE"
                };

                session.TransitionTo("STARTING");
                session.TransitionTo("ACTIVE");
                session.NormalizeAndValidate();

                await _sessionRepository.AddAsync(session, cancellationToken);

                var createdEvent = new AuditEvent
                {
                    EventId = Guid.NewGuid(),
                    EventType = nameof(SessionCreated),
                    EventVersion = 1,
                    Timestamp = DateTime.UtcNow,
                    Payload = JsonSerializer.Serialize(new SessionCreated(
                        session.Id,
                        session.GamerId,
                        session.WorkstationId,
                        session.OrganizationId,
                        session.SiteId,
                        session.ReservationId,
                        session.Status,
                        session.StartedAt,
                        DateTime.UtcNow
                    ))
                };
                await _auditEventRepository.AddAsync(createdEvent, cancellationToken);

                var startedEvent = new AuditEvent
                {
                    EventId = Guid.NewGuid(),
                    EventType = nameof(SessionStarted),
                    EventVersion = 1,
                    Timestamp = DateTime.UtcNow,
                    Payload = JsonSerializer.Serialize(new SessionStarted(
                        session.Id,
                        session.GamerId,
                        session.WorkstationId,
                        session.SiteId,
                        session.Status,
                        DateTime.UtcNow
                    ))
                };
                await _auditEventRepository.AddAsync(startedEvent, cancellationToken);

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return Result<SessionResponseDto>.Success(MapToDto(session));
            }
            catch (InvalidDomainException domainEx)
            {
                return Result<SessionResponseDto>.Failure(domainEx.ErrorCode, domainEx.Message);
            }
            catch (Exception ex)
            {
                return Result<SessionResponseDto>.Failure("START_SESSION_FAILED", ex.Message);
            }
        }

        private static SessionResponseDto MapToDto(Session s)
        {
            return new SessionResponseDto
            {
                SessionId = s.Id,
                OrganizationId = s.OrganizationId,
                SiteId = s.SiteId,
                WorkstationId = s.WorkstationId,
                GamerId = s.GamerId,
                ReservationId = s.ReservationId,
                Status = s.Status,
                StartedAt = s.StartedAt,
                PausedAt = s.PausedAt,
                EndedAt = s.EndedAt,
                CreatedAt = s.CreatedAt,
                UpdatedAt = s.UpdatedAt
            };
        }
    }

    public class PauseSessionCommandHandler : ICommandHandler<PauseSessionCommand, SessionResponseDto>
    {
        private readonly IRepository<Session> _sessionRepository;
        private readonly IRepository<AuditEvent> _auditEventRepository;
        private readonly IUnitOfWork _unitOfWork;

        public PauseSessionCommandHandler(
            IRepository<Session> sessionRepository,
            IRepository<AuditEvent> auditEventRepository,
            IUnitOfWork unitOfWork)
        {
            _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
            _auditEventRepository = auditEventRepository ?? throw new ArgumentNullException(nameof(auditEventRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        }

        public async Task<Result<SessionResponseDto>> HandleAsync(PauseSessionCommand command, CancellationToken cancellationToken = default)
        {
            try
            {
                var validator = new PauseSessionCommandValidator();
                var validationResult = await validator.ValidateAsync(command, cancellationToken);
                if (!validationResult.IsValid)
                {
                    var firstError = validationResult.Errors.First();
                    return Result<SessionResponseDto>.Failure(firstError.ErrorCode ?? "VALIDATION_FAILED", firstError.ErrorMessage);
                }

                var session = await _sessionRepository.GetByIdAsync(command.SessionId, track: true, cancellationToken);
                if (session == null)
                {
                    return Result<SessionResponseDto>.Failure("NOT_FOUND", $"Session with ID '{command.SessionId}' not found.");
                }

                session.TransitionTo("PAUSED");
                _sessionRepository.Update(session);

                var auditEvent = new AuditEvent
                {
                    EventId = Guid.NewGuid(),
                    EventType = nameof(SessionPaused),
                    EventVersion = 1,
                    Timestamp = DateTime.UtcNow,
                    Payload = JsonSerializer.Serialize(new SessionPaused(
                        session.Id,
                        session.GamerId,
                        session.WorkstationId,
                        session.Status,
                        DateTime.UtcNow
                    ))
                };
                await _auditEventRepository.AddAsync(auditEvent, cancellationToken);

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return Result<SessionResponseDto>.Success(MapToDto(session));
            }
            catch (InvalidDomainException domainEx)
            {
                return Result<SessionResponseDto>.Failure(domainEx.ErrorCode, domainEx.Message);
            }
            catch (Exception ex)
            {
                return Result<SessionResponseDto>.Failure("PAUSE_SESSION_FAILED", ex.Message);
            }
        }

        private static SessionResponseDto MapToDto(Session s)
        {
            return new SessionResponseDto
            {
                SessionId = s.Id,
                OrganizationId = s.OrganizationId,
                SiteId = s.SiteId,
                WorkstationId = s.WorkstationId,
                GamerId = s.GamerId,
                ReservationId = s.ReservationId,
                Status = s.Status,
                StartedAt = s.StartedAt,
                PausedAt = s.PausedAt,
                EndedAt = s.EndedAt,
                CreatedAt = s.CreatedAt,
                UpdatedAt = s.UpdatedAt
            };
        }
    }

    public class ResumeSessionCommandHandler : ICommandHandler<ResumeSessionCommand, SessionResponseDto>
    {
        private readonly IRepository<Session> _sessionRepository;
        private readonly IRepository<AuditEvent> _auditEventRepository;
        private readonly IUnitOfWork _unitOfWork;

        public ResumeSessionCommandHandler(
            IRepository<Session> sessionRepository,
            IRepository<AuditEvent> auditEventRepository,
            IUnitOfWork unitOfWork)
        {
            _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
            _auditEventRepository = auditEventRepository ?? throw new ArgumentNullException(nameof(auditEventRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        }

        public async Task<Result<SessionResponseDto>> HandleAsync(ResumeSessionCommand command, CancellationToken cancellationToken = default)
        {
            try
            {
                var validator = new ResumeSessionCommandValidator();
                var validationResult = await validator.ValidateAsync(command, cancellationToken);
                if (!validationResult.IsValid)
                {
                    var firstError = validationResult.Errors.First();
                    return Result<SessionResponseDto>.Failure(firstError.ErrorCode ?? "VALIDATION_FAILED", firstError.ErrorMessage);
                }

                var session = await _sessionRepository.GetByIdAsync(command.SessionId, track: true, cancellationToken);
                if (session == null)
                {
                    return Result<SessionResponseDto>.Failure("NOT_FOUND", $"Session with ID '{command.SessionId}' not found.");
                }

                session.TransitionTo("ACTIVE");
                _sessionRepository.Update(session);

                var auditEvent = new AuditEvent
                {
                    EventId = Guid.NewGuid(),
                    EventType = nameof(SessionResumed),
                    EventVersion = 1,
                    Timestamp = DateTime.UtcNow,
                    Payload = JsonSerializer.Serialize(new SessionResumed(
                        session.Id,
                        session.GamerId,
                        session.WorkstationId,
                        session.Status,
                        DateTime.UtcNow
                    ))
                };
                await _auditEventRepository.AddAsync(auditEvent, cancellationToken);

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return Result<SessionResponseDto>.Success(MapToDto(session));
            }
            catch (InvalidDomainException domainEx)
            {
                return Result<SessionResponseDto>.Failure(domainEx.ErrorCode, domainEx.Message);
            }
            catch (Exception ex)
            {
                return Result<SessionResponseDto>.Failure("RESUME_SESSION_FAILED", ex.Message);
            }
        }

        private static SessionResponseDto MapToDto(Session s)
        {
            return new SessionResponseDto
            {
                SessionId = s.Id,
                OrganizationId = s.OrganizationId,
                SiteId = s.SiteId,
                WorkstationId = s.WorkstationId,
                GamerId = s.GamerId,
                ReservationId = s.ReservationId,
                Status = s.Status,
                StartedAt = s.StartedAt,
                PausedAt = s.PausedAt,
                EndedAt = s.EndedAt,
                CreatedAt = s.CreatedAt,
                UpdatedAt = s.UpdatedAt
            };
        }
    }

    public class StopSessionCommandHandler : ICommandHandler<StopSessionCommand, SessionResponseDto>
    {
        private readonly IRepository<Session> _sessionRepository;
        private readonly IRepository<Reservation> _reservationRepository;
        private readonly IRepository<AuditEvent> _auditEventRepository;
        private readonly IUnitOfWork _unitOfWork;

        public StopSessionCommandHandler(
            IRepository<Session> sessionRepository,
            IRepository<Reservation> reservationRepository,
            IRepository<AuditEvent> auditEventRepository,
            IUnitOfWork unitOfWork)
        {
            _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
            _reservationRepository = reservationRepository ?? throw new ArgumentNullException(nameof(reservationRepository));
            _auditEventRepository = auditEventRepository ?? throw new ArgumentNullException(nameof(auditEventRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        }

        public async Task<Result<SessionResponseDto>> HandleAsync(StopSessionCommand command, CancellationToken cancellationToken = default)
        {
            try
            {
                var validator = new StopSessionCommandValidator();
                var validationResult = await validator.ValidateAsync(command, cancellationToken);
                if (!validationResult.IsValid)
                {
                    var firstError = validationResult.Errors.First();
                    return Result<SessionResponseDto>.Failure(firstError.ErrorCode ?? "VALIDATION_FAILED", firstError.ErrorMessage);
                }

                var session = await _sessionRepository.GetByIdAsync(command.SessionId, track: true, cancellationToken);
                if (session == null)
                {
                    return Result<SessionResponseDto>.Failure("NOT_FOUND", $"Session with ID '{command.SessionId}' not found.");
                }

                if (session.Status == "ENDED" || session.Status == "EXPIRED" || session.Status == "CANCELLED" || session.Status == "TERMINATED")
                {
                    return Result<SessionResponseDto>.Success(MapToDto(session));
                }

                session.TransitionTo("ENDING");
                session.TransitionTo("ENDED");
                _sessionRepository.Update(session);

                if (session.ReservationId.HasValue && session.ReservationId.Value != Guid.Empty)
                {
                    var reservation = await _reservationRepository.GetByIdAsync(session.ReservationId.Value, track: true, cancellationToken);
                    if (reservation != null && reservation.Status == "ACTIVE")
                    {
                        reservation.TransitionTo("COMPLETED");
                        _reservationRepository.Update(reservation);
                    }
                }

                var auditEvent = new AuditEvent
                {
                    EventId = Guid.NewGuid(),
                    EventType = nameof(SessionStopped),
                    EventVersion = 1,
                    Timestamp = DateTime.UtcNow,
                    Payload = JsonSerializer.Serialize(new SessionStopped(
                        session.Id,
                        session.GamerId,
                        session.WorkstationId,
                        session.Status,
                        DateTime.UtcNow
                    ))
                };
                await _auditEventRepository.AddAsync(auditEvent, cancellationToken);

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return Result<SessionResponseDto>.Success(MapToDto(session));
            }
            catch (InvalidDomainException domainEx)
            {
                return Result<SessionResponseDto>.Failure(domainEx.ErrorCode, domainEx.Message);
            }
            catch (Exception ex)
            {
                return Result<SessionResponseDto>.Failure("STOP_SESSION_FAILED", ex.Message);
            }
        }

        private static SessionResponseDto MapToDto(Session s)
        {
            return new SessionResponseDto
            {
                SessionId = s.Id,
                OrganizationId = s.OrganizationId,
                SiteId = s.SiteId,
                WorkstationId = s.WorkstationId,
                GamerId = s.GamerId,
                ReservationId = s.ReservationId,
                Status = s.Status,
                StartedAt = s.StartedAt,
                PausedAt = s.PausedAt,
                EndedAt = s.EndedAt,
                CreatedAt = s.CreatedAt,
                UpdatedAt = s.UpdatedAt
            };
        }
    }

    public class CancelSessionCommandHandler : ICommandHandler<CancelSessionCommand, SessionResponseDto>
    {
        private readonly IRepository<Session> _sessionRepository;
        private readonly IRepository<Reservation> _reservationRepository;
        private readonly IRepository<AuditEvent> _auditEventRepository;
        private readonly IUnitOfWork _unitOfWork;

        public CancelSessionCommandHandler(
            IRepository<Session> sessionRepository,
            IRepository<Reservation> reservationRepository,
            IRepository<AuditEvent> auditEventRepository,
            IUnitOfWork unitOfWork)
        {
            _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
            _reservationRepository = reservationRepository ?? throw new ArgumentNullException(nameof(reservationRepository));
            _auditEventRepository = auditEventRepository ?? throw new ArgumentNullException(nameof(auditEventRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        }

        public async Task<Result<SessionResponseDto>> HandleAsync(CancelSessionCommand command, CancellationToken cancellationToken = default)
        {
            try
            {
                var validator = new CancelSessionCommandValidator();
                var validationResult = await validator.ValidateAsync(command, cancellationToken);
                if (!validationResult.IsValid)
                {
                    var firstError = validationResult.Errors.First();
                    return Result<SessionResponseDto>.Failure(firstError.ErrorCode ?? "VALIDATION_FAILED", firstError.ErrorMessage);
                }

                var session = await _sessionRepository.GetByIdAsync(command.SessionId, track: true, cancellationToken);
                if (session == null)
                {
                    return Result<SessionResponseDto>.Failure("NOT_FOUND", $"Session with ID '{command.SessionId}' not found.");
                }

                if (session.Status == "CANCELLED" || session.Status == "ENDED" || session.Status == "EXPIRED" || session.Status == "TERMINATED")
                {
                    return Result<SessionResponseDto>.Success(MapToDto(session));
                }

                session.TransitionTo("CANCELLED");
                _sessionRepository.Update(session);

                if (session.ReservationId.HasValue && session.ReservationId.Value != Guid.Empty)
                {
                    var reservation = await _reservationRepository.GetByIdAsync(session.ReservationId.Value, track: true, cancellationToken);
                    if (reservation != null && (reservation.Status == "ACTIVE" || reservation.Status == "CONFIRMED" || reservation.Status == "PENDING"))
                    {
                        reservation.TransitionTo("CANCELLED");
                        _reservationRepository.Update(reservation);
                    }
                }

                var auditEvent = new AuditEvent
                {
                    EventId = Guid.NewGuid(),
                    EventType = nameof(SessionCancelled),
                    EventVersion = 1,
                    Timestamp = DateTime.UtcNow,
                    Payload = JsonSerializer.Serialize(new SessionCancelled(
                        session.Id,
                        session.GamerId,
                        session.WorkstationId,
                        session.Status,
                        DateTime.UtcNow
                    ))
                };
                await _auditEventRepository.AddAsync(auditEvent, cancellationToken);

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return Result<SessionResponseDto>.Success(MapToDto(session));
            }
            catch (InvalidDomainException domainEx)
            {
                return Result<SessionResponseDto>.Failure(domainEx.ErrorCode, domainEx.Message);
            }
            catch (Exception ex)
            {
                return Result<SessionResponseDto>.Failure("CANCEL_SESSION_FAILED", ex.Message);
            }
        }

        private static SessionResponseDto MapToDto(Session s)
        {
            return new SessionResponseDto
            {
                SessionId = s.Id,
                OrganizationId = s.OrganizationId,
                SiteId = s.SiteId,
                WorkstationId = s.WorkstationId,
                GamerId = s.GamerId,
                ReservationId = s.ReservationId,
                Status = s.Status,
                StartedAt = s.StartedAt,
                PausedAt = s.PausedAt,
                EndedAt = s.EndedAt,
                CreatedAt = s.CreatedAt,
                UpdatedAt = s.UpdatedAt
            };
        }
    }

    public class TerminateSessionCommandHandler : ICommandHandler<TerminateSessionCommand, SessionResponseDto>
    {
        private readonly IRepository<Session> _sessionRepository;
        private readonly IRepository<AuditEvent> _auditEventRepository;
        private readonly IUnitOfWork _unitOfWork;

        public TerminateSessionCommandHandler(
            IRepository<Session> sessionRepository,
            IRepository<AuditEvent> auditEventRepository,
            IUnitOfWork unitOfWork)
        {
            _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
            _auditEventRepository = auditEventRepository ?? throw new ArgumentNullException(nameof(auditEventRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        }

        public async Task<Result<SessionResponseDto>> HandleAsync(TerminateSessionCommand command, CancellationToken cancellationToken = default)
        {
            try
            {
                var validator = new TerminateSessionCommandValidator();
                var validationResult = await validator.ValidateAsync(command, cancellationToken);
                if (!validationResult.IsValid)
                {
                    var firstError = validationResult.Errors.First();
                    return Result<SessionResponseDto>.Failure(firstError.ErrorCode ?? "VALIDATION_FAILED", firstError.ErrorMessage);
                }

                var session = await _sessionRepository.GetByIdAsync(command.SessionId, track: true, cancellationToken);
                if (session == null)
                {
                    return Result<SessionResponseDto>.Failure("NOT_FOUND", $"Session with ID '{command.SessionId}' not found.");
                }

                if (session.Status == "TERMINATED" || session.Status == "ENDED" || session.Status == "EXPIRED" || session.Status == "CANCELLED")
                {
                    return Result<SessionResponseDto>.Success(MapToDto(session));
                }

                session.TransitionTo("TERMINATED");
                _sessionRepository.Update(session);

                var auditEvent = new AuditEvent
                {
                    EventId = Guid.NewGuid(),
                    EventType = nameof(SessionTerminated),
                    EventVersion = 1,
                    Timestamp = DateTime.UtcNow,
                    Payload = JsonSerializer.Serialize(new SessionTerminated(
                        session.Id,
                        session.GamerId,
                        session.WorkstationId,
                        session.Status,
                        command.Reason ?? "Terminated by administrator",
                        DateTime.UtcNow
                    ))
                };
                await _auditEventRepository.AddAsync(auditEvent, cancellationToken);

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return Result<SessionResponseDto>.Success(MapToDto(session));
            }
            catch (InvalidDomainException domainEx)
            {
                return Result<SessionResponseDto>.Failure(domainEx.ErrorCode, domainEx.Message);
            }
            catch (Exception ex)
            {
                return Result<SessionResponseDto>.Failure("TERMINATE_SESSION_FAILED", ex.Message);
            }
        }

        private static SessionResponseDto MapToDto(Session s)
        {
            return new SessionResponseDto
            {
                SessionId = s.Id,
                OrganizationId = s.OrganizationId,
                SiteId = s.SiteId,
                WorkstationId = s.WorkstationId,
                GamerId = s.GamerId,
                ReservationId = s.ReservationId,
                Status = s.Status,
                StartedAt = s.StartedAt,
                PausedAt = s.PausedAt,
                EndedAt = s.EndedAt,
                CreatedAt = s.CreatedAt,
                UpdatedAt = s.UpdatedAt
            };
        }
    }

    public class GetSessionQueryHandler : IQueryHandler<GetSessionQuery, SessionResponseDto>
    {
        private readonly IRepository<Session> _sessionRepository;

        public GetSessionQueryHandler(IRepository<Session> sessionRepository)
        {
            _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
        }

        public async Task<Result<SessionResponseDto>> HandleAsync(GetSessionQuery query, CancellationToken cancellationToken = default)
        {
            var validator = new GetSessionQueryValidator();
            var validationResult = await validator.ValidateAsync(query, cancellationToken);
            if (!validationResult.IsValid)
            {
                var firstError = validationResult.Errors.First();
                return Result<SessionResponseDto>.Failure(firstError.ErrorCode ?? "VALIDATION_FAILED", firstError.ErrorMessage);
            }

            var session = await _sessionRepository.GetByIdAsync(query.SessionId, track: false, cancellationToken);
            if (session == null)
            {
                return Result<SessionResponseDto>.Failure("NOT_FOUND", $"Session with ID '{query.SessionId}' not found.");
            }

            return Result<SessionResponseDto>.Success(MapToDto(session));
        }

        private static SessionResponseDto MapToDto(Session s)
        {
            return new SessionResponseDto
            {
                SessionId = s.Id,
                OrganizationId = s.OrganizationId,
                SiteId = s.SiteId,
                WorkstationId = s.WorkstationId,
                GamerId = s.GamerId,
                ReservationId = s.ReservationId,
                Status = s.Status,
                StartedAt = s.StartedAt,
                PausedAt = s.PausedAt,
                EndedAt = s.EndedAt,
                CreatedAt = s.CreatedAt,
                UpdatedAt = s.UpdatedAt
            };
        }
    }

    public class GetActiveSessionByWorkstationQueryHandler : IQueryHandler<GetActiveSessionByWorkstationQuery, SessionResponseDto?>
    {
        private readonly IRepository<Session> _sessionRepository;

        public GetActiveSessionByWorkstationQueryHandler(IRepository<Session> sessionRepository)
        {
            _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
        }

        public async Task<Result<SessionResponseDto?>> HandleAsync(GetActiveSessionByWorkstationQuery query, CancellationToken cancellationToken = default)
        {
            var validator = new GetActiveSessionByWorkstationQueryValidator();
            var validationResult = await validator.ValidateAsync(query, cancellationToken);
            if (!validationResult.IsValid)
            {
                var firstError = validationResult.Errors.First();
                return Result<SessionResponseDto?>.Failure(firstError.ErrorCode ?? "VALIDATION_FAILED", firstError.ErrorMessage);
            }

            var session = await _sessionRepository.FirstOrDefaultAsync(
                s => s.WorkstationId == query.WorkstationId &&
                     (s.Status == "STARTING" || s.Status == "ACTIVE" || s.Status == "PAUSED" || s.Status == "ENDING"),
                track: false,
                cancellationToken);

            if (session == null)
            {
                return Result<SessionResponseDto?>.Success(null);
            }

            return Result<SessionResponseDto?>.Success(MapToDto(session));
        }

        private static SessionResponseDto MapToDto(Session s)
        {
            return new SessionResponseDto
            {
                SessionId = s.Id,
                OrganizationId = s.OrganizationId,
                SiteId = s.SiteId,
                WorkstationId = s.WorkstationId,
                GamerId = s.GamerId,
                ReservationId = s.ReservationId,
                Status = s.Status,
                StartedAt = s.StartedAt,
                PausedAt = s.PausedAt,
                EndedAt = s.EndedAt,
                CreatedAt = s.CreatedAt,
                UpdatedAt = s.UpdatedAt
            };
        }
    }

    public class GetActiveSessionByGamerQueryHandler : IQueryHandler<GetActiveSessionByGamerQuery, SessionResponseDto?>
    {
        private readonly IRepository<Session> _sessionRepository;

        public GetActiveSessionByGamerQueryHandler(IRepository<Session> sessionRepository)
        {
            _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
        }

        public async Task<Result<SessionResponseDto?>> HandleAsync(GetActiveSessionByGamerQuery query, CancellationToken cancellationToken = default)
        {
            var validator = new GetActiveSessionByGamerQueryValidator();
            var validationResult = await validator.ValidateAsync(query, cancellationToken);
            if (!validationResult.IsValid)
            {
                var firstError = validationResult.Errors.First();
                return Result<SessionResponseDto?>.Failure(firstError.ErrorCode ?? "VALIDATION_FAILED", firstError.ErrorMessage);
            }

            var session = await _sessionRepository.FirstOrDefaultAsync(
                s => s.GamerId == query.GamerId &&
                     (s.Status == "STARTING" || s.Status == "ACTIVE" || s.Status == "PAUSED" || s.Status == "ENDING"),
                track: false,
                cancellationToken);

            if (session == null)
            {
                return Result<SessionResponseDto?>.Success(null);
            }

            return Result<SessionResponseDto?>.Success(MapToDto(session));
        }

        private static SessionResponseDto MapToDto(Session s)
        {
            return new SessionResponseDto
            {
                SessionId = s.Id,
                OrganizationId = s.OrganizationId,
                SiteId = s.SiteId,
                WorkstationId = s.WorkstationId,
                GamerId = s.GamerId,
                ReservationId = s.ReservationId,
                Status = s.Status,
                StartedAt = s.StartedAt,
                PausedAt = s.PausedAt,
                EndedAt = s.EndedAt,
                CreatedAt = s.CreatedAt,
                UpdatedAt = s.UpdatedAt
            };
        }
    }
}
