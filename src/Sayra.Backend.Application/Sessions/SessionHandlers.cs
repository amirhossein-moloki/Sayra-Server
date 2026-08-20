using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Domain.Events;
using Sayra.Backend.Domain.Exceptions;
using Sayra.Backend.Shared;

namespace Sayra.Backend.Application.Sessions
{
    public class StartSessionCommandHandler : ICommandHandler<StartSessionCommand, SessionResponseDto>
    {
        private readonly IRepository<Session> _sessionRepository;
        private readonly IRepository<SessionSegment> _segmentRepository;
        private readonly IRepository<Workstation> _workstationRepository;
        private readonly IRepository<Reservation> _reservationRepository;
        private readonly IRepository<AuditEvent> _auditEventRepository;
        private readonly ISessionStateTransitionService _transitionService;
        private readonly Sayra.Backend.Application.Pricing.IRateResolver _rateResolver;
        private readonly Sayra.Backend.Application.Pricing.IRateSnapshotService _rateSnapshotService;
        private readonly IUnitOfWork _unitOfWork;

        public StartSessionCommandHandler(
            IRepository<Session> sessionRepository,
            IRepository<SessionSegment> segmentRepository,
            IRepository<Workstation> workstationRepository,
            IRepository<Reservation> reservationRepository,
            IRepository<AuditEvent> auditEventRepository,
            ISessionStateTransitionService transitionService,
            Sayra.Backend.Application.Pricing.IRateResolver rateResolver,
            Sayra.Backend.Application.Pricing.IRateSnapshotService rateSnapshotService,
            IUnitOfWork unitOfWork)
        {
            _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
            _segmentRepository = segmentRepository ?? throw new ArgumentNullException(nameof(segmentRepository));
            _workstationRepository = workstationRepository ?? throw new ArgumentNullException(nameof(workstationRepository));
            _reservationRepository = reservationRepository ?? throw new ArgumentNullException(nameof(reservationRepository));
            _auditEventRepository = auditEventRepository ?? throw new ArgumentNullException(nameof(auditEventRepository));
            _transitionService = transitionService ?? throw new ArgumentNullException(nameof(transitionService));
            _rateResolver = rateResolver ?? throw new ArgumentNullException(nameof(rateResolver));
            _rateSnapshotService = rateSnapshotService ?? throw new ArgumentNullException(nameof(rateSnapshotService));
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

                return await _unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    if (command.ReservationId.HasValue && command.ReservationId.Value != Guid.Empty)
                    {
                        var reservation = await _reservationRepository.GetByIdAsync(command.ReservationId.Value, track: true, cancellationToken);
                        if (reservation != null && reservation.Status == "CONFIRMED")
                        {
                            reservation.TransitionTo("ACTIVE");
                            _reservationRepository.Update(reservation);
                        }
                    }

                    var serverNow = DateTime.UtcNow;

                    var session = new Session
                    {
                        OrganizationId = workstation.OrganizationEntityId.Value,
                        SiteId = workstation.SiteEntityId.Value,
                        WorkstationId = command.WorkstationId,
                        GamerId = command.GamerId,
                        ReservationId = command.ReservationId,
                        StartedAt = serverNow,
                        Status = "IDLE"
                    };

                    session.TransitionTo("STARTING");
                    session.TransitionTo("ACTIVE");
                    session.NormalizeAndValidate();

                    await _sessionRepository.AddAsync(session, cancellationToken);

                    var initialSegment = new SessionSegment
                    {
                        SessionId = session.Id,
                        Type = "ACTIVE",
                        StartedAtUtc = serverNow,
                        EndedAtUtc = null,
                        CreatedAt = serverNow
                    };
                    initialSegment.NormalizeAndValidate();
                    await _segmentRepository.AddAsync(initialSegment, cancellationToken);

                    // Pricing rate resolution and snapshot creation
                    try
                    {
                        var rateRequest = new ResolveRateRequestDto
                        {
                            SiteId = workstation.SiteEntityId.Value,
                            ZoneId = workstation.ZoneEntityId,
                            WorkstationId = workstation.Id,
                            Timestamp = serverNow
                        };
                        var resolvedRate = await _rateResolver.ResolveRateAsync(rateRequest, cancellationToken);
                        if (resolvedRate != null)
                        {
                            await _rateSnapshotService.CreateSnapshotAsync(
                                session.Id,
                                resolvedRate.PricingPlanId,
                                resolvedRate.PricingRuleId,
                                resolvedRate.RateAmount,
                                resolvedRate.Currency,
                                resolvedRate.RuleReference,
                                serverNow,
                                cancellationToken);
                        }
                    }
                    catch (InvalidDomainException ex) when (ex.ErrorCode == "PRICING_PLAN_NOT_FOUND" || ex.ErrorCode == "NO_MATCHING_RULE")
                    {
                        // Default fallback if no pricing plan/rule is active for site yet
                    }

                    var createdEvent = new AuditEvent
                    {
                        EventId = Guid.NewGuid(),
                        EventType = nameof(SessionCreated),
                        EventVersion = 1,
                        Timestamp = serverNow,
                        Payload = JsonSerializer.Serialize(new SessionCreated(
                            session.Id,
                            session.GamerId,
                            session.WorkstationId,
                            session.OrganizationId,
                            session.SiteId,
                            session.ReservationId,
                            session.Status,
                            session.StartedAt,
                            serverNow
                        ))
                    };
                    await _auditEventRepository.AddAsync(createdEvent, cancellationToken);

                    var startedEvent = new AuditEvent
                    {
                        EventId = Guid.NewGuid(),
                        EventType = nameof(SessionStarted),
                        EventVersion = 1,
                        Timestamp = serverNow,
                        Payload = JsonSerializer.Serialize(new SessionStarted(
                            session.Id,
                            session.GamerId,
                            session.WorkstationId,
                            session.SiteId,
                            session.Status,
                            serverNow
                        ))
                    };
                    await _auditEventRepository.AddAsync(startedEvent, cancellationToken);

                    return Result<SessionResponseDto>.Success(MapToDto(session));
                }, cancellationToken);
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
        private readonly IRepository<SessionSegment> _segmentRepository;
        private readonly IRepository<AuditEvent> _auditEventRepository;
        private readonly IUnitOfWork _unitOfWork;

        public PauseSessionCommandHandler(
            IRepository<Session> sessionRepository,
            IRepository<SessionSegment> segmentRepository,
            IRepository<AuditEvent> auditEventRepository,
            IUnitOfWork unitOfWork)
        {
            _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
            _segmentRepository = segmentRepository ?? throw new ArgumentNullException(nameof(segmentRepository));
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

                if (session.Status == "PAUSED")
                {
                    return Result<SessionResponseDto>.Success(MapToDto(session));
                }

                var serverNow = DateTime.UtcNow;

                var openSegment = await _segmentRepository.FirstOrDefaultAsync(
                    s => s.SessionId == session.Id && s.EndedAtUtc == null,
                    track: true,
                    cancellationToken);

                if (openSegment != null)
                {
                    openSegment.EndedAtUtc = serverNow;
                    _segmentRepository.Update(openSegment);
                }

                var pausedSegment = new SessionSegment
                {
                    SessionId = session.Id,
                    Type = "PAUSED",
                    StartedAtUtc = serverNow,
                    EndedAtUtc = null,
                    CreatedAt = serverNow
                };
                pausedSegment.NormalizeAndValidate();
                await _segmentRepository.AddAsync(pausedSegment, cancellationToken);

                session.TransitionTo("PAUSED");
                _sessionRepository.Update(session);

                var auditEvent = new AuditEvent
                {
                    EventId = Guid.NewGuid(),
                    EventType = nameof(SessionPaused),
                    EventVersion = 1,
                    Timestamp = serverNow,
                    Payload = JsonSerializer.Serialize(new SessionPaused(
                        session.Id,
                        session.GamerId,
                        session.WorkstationId,
                        session.Status,
                        serverNow
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
        private readonly IRepository<SessionSegment> _segmentRepository;
        private readonly IRepository<AuditEvent> _auditEventRepository;
        private readonly IUnitOfWork _unitOfWork;

        public ResumeSessionCommandHandler(
            IRepository<Session> sessionRepository,
            IRepository<SessionSegment> segmentRepository,
            IRepository<AuditEvent> auditEventRepository,
            IUnitOfWork unitOfWork)
        {
            _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
            _segmentRepository = segmentRepository ?? throw new ArgumentNullException(nameof(segmentRepository));
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

                if (session.Status == "ACTIVE")
                {
                    return Result<SessionResponseDto>.Success(MapToDto(session));
                }

                var serverNow = DateTime.UtcNow;

                var openSegment = await _segmentRepository.FirstOrDefaultAsync(
                    s => s.SessionId == session.Id && s.EndedAtUtc == null,
                    track: true,
                    cancellationToken);

                if (openSegment != null)
                {
                    openSegment.EndedAtUtc = serverNow;
                    _segmentRepository.Update(openSegment);
                }

                var activeSegment = new SessionSegment
                {
                    SessionId = session.Id,
                    Type = "ACTIVE",
                    StartedAtUtc = serverNow,
                    EndedAtUtc = null,
                    CreatedAt = serverNow
                };
                activeSegment.NormalizeAndValidate();
                await _segmentRepository.AddAsync(activeSegment, cancellationToken);

                session.TransitionTo("ACTIVE");
                _sessionRepository.Update(session);

                var auditEvent = new AuditEvent
                {
                    EventId = Guid.NewGuid(),
                    EventType = nameof(SessionResumed),
                    EventVersion = 1,
                    Timestamp = serverNow,
                    Payload = JsonSerializer.Serialize(new SessionResumed(
                        session.Id,
                        session.GamerId,
                        session.WorkstationId,
                        session.Status,
                        serverNow
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
        private readonly IRepository<SessionSegment> _segmentRepository;
        private readonly IRepository<SessionExtension> _extensionRepository;
        private readonly IRepository<Reservation> _reservationRepository;
        private readonly IRepository<GamerAccount> _accountRepository;
        private readonly ISessionTimeCalculator _timeCalculator;
        private readonly Sayra.Backend.Application.Pricing.IRateSnapshotService _rateSnapshotService;
        private readonly Sayra.Backend.Application.Financial.IFinancialTransactionService _financialTransactionService;
        private readonly IRepository<AuditEvent> _auditEventRepository;
        private readonly IUnitOfWork _unitOfWork;

        public StopSessionCommandHandler(
            IRepository<Session> sessionRepository,
            IRepository<SessionSegment> segmentRepository,
            IRepository<SessionExtension> extensionRepository,
            IRepository<Reservation> reservationRepository,
            IRepository<GamerAccount> accountRepository,
            ISessionTimeCalculator timeCalculator,
            Sayra.Backend.Application.Pricing.IRateSnapshotService rateSnapshotService,
            Sayra.Backend.Application.Financial.IFinancialTransactionService financialTransactionService,
            IRepository<AuditEvent> auditEventRepository,
            IUnitOfWork unitOfWork)
        {
            _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
            _segmentRepository = segmentRepository ?? throw new ArgumentNullException(nameof(segmentRepository));
            _extensionRepository = extensionRepository ?? throw new ArgumentNullException(nameof(extensionRepository));
            _reservationRepository = reservationRepository ?? throw new ArgumentNullException(nameof(reservationRepository));
            _accountRepository = accountRepository ?? throw new ArgumentNullException(nameof(accountRepository));
            _timeCalculator = timeCalculator ?? throw new ArgumentNullException(nameof(timeCalculator));
            _rateSnapshotService = rateSnapshotService ?? throw new ArgumentNullException(nameof(rateSnapshotService));
            _financialTransactionService = financialTransactionService ?? throw new ArgumentNullException(nameof(financialTransactionService));
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

                return await _unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    var serverNow = DateTime.UtcNow;

                    var openSegment = await _segmentRepository.FirstOrDefaultAsync(
                        s => s.SessionId == session.Id && s.EndedAtUtc == null,
                        track: true,
                        cancellationToken);

                    if (openSegment != null)
                    {
                        openSegment.EndedAtUtc = serverNow;
                        _segmentRepository.Update(openSegment);
                    }

                    // Calculate timing and billing
                    var segments = await _segmentRepository.FindAsync(s => s.SessionId == session.Id, track: false, cancellationToken);
                    var timingSnapshot = _timeCalculator.CalculateTiming(session, segments, serverNow, null);

                    var rateSnapshot = await _rateSnapshotService.GetSnapshotBySessionIdAsync(session.Id, cancellationToken);
                    decimal hourlyRate = rateSnapshot?.RateAmount ?? 10.00m;
                    string currency = rateSnapshot?.Currency ?? "SAY";

                    decimal totalUsageCost = Math.Round((hourlyRate / 60m) * (decimal)timingSnapshot.ConsumedDuration.TotalMinutes, 4);

                    // Subtract costs already paid via prepaid session extensions
                    var existingExtensions = await _extensionRepository.FindAsync(e => e.SessionId == session.Id, track: false, cancellationToken);
                    decimal prepaidExtensionsCost = existingExtensions.Sum(e => e.Cost);

                    decimal netUsageCharge = Math.Max(0m, totalUsageCost - prepaidExtensionsCost);

                    if (netUsageCharge > 0)
                    {
                        var gamerAccount = await _accountRepository.FirstOrDefaultAsync(
                            a => a.GamerEntityId == session.GamerId,
                            track: false,
                            cancellationToken);

                        if (gamerAccount != null)
                        {
                            var txDto = new ProcessTransactionRequestDto
                            {
                                GamerAccountId = gamerAccount.Id,
                                OperationType = "USAGE_CHARGE",
                                Amount = netUsageCharge,
                                Currency = currency,
                                IdempotencyKey = $"TX-STOP-{session.Id:N}",
                                ReferenceId = session.Id.ToString(),
                                Description = $"Net usage charge for Session {session.Id} (Total: {totalUsageCost}, Prepaid: {prepaidExtensionsCost})"
                            };

                            var txResult = await _financialTransactionService.ProcessTransactionAsync(txDto, cancellationToken);
                            if (!txResult.IsSuccess)
                            {
                                return Result<SessionResponseDto>.Failure(txResult.ErrorCode ?? "FINANCIAL_TRANSACTION_FAILED", $"Financial charge failed: [{txResult.ErrorCode}] {txResult.ErrorMessage}");
                            }
                        }
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
                        Timestamp = serverNow,
                        Payload = JsonSerializer.Serialize(new SessionStopped(
                            session.Id,
                            session.GamerId,
                            session.WorkstationId,
                            session.Status,
                            serverNow
                        ))
                    };
                    await _auditEventRepository.AddAsync(auditEvent, cancellationToken);

                    return Result<SessionResponseDto>.Success(MapToDto(session));
                }, cancellationToken);
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
        private readonly IRepository<SessionSegment> _segmentRepository;
        private readonly IRepository<Reservation> _reservationRepository;
        private readonly IRepository<AuditEvent> _auditEventRepository;
        private readonly IUnitOfWork _unitOfWork;

        public CancelSessionCommandHandler(
            IRepository<Session> sessionRepository,
            IRepository<SessionSegment> segmentRepository,
            IRepository<Reservation> reservationRepository,
            IRepository<AuditEvent> auditEventRepository,
            IUnitOfWork unitOfWork)
        {
            _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
            _segmentRepository = segmentRepository ?? throw new ArgumentNullException(nameof(segmentRepository));
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

                var serverNow = DateTime.UtcNow;

                var openSegment = await _segmentRepository.FirstOrDefaultAsync(
                    s => s.SessionId == session.Id && s.EndedAtUtc == null,
                    track: true,
                    cancellationToken);

                if (openSegment != null)
                {
                    openSegment.EndedAtUtc = serverNow;
                    _segmentRepository.Update(openSegment);
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
                    Timestamp = serverNow,
                    Payload = JsonSerializer.Serialize(new SessionCancelled(
                        session.Id,
                        session.GamerId,
                        session.WorkstationId,
                        session.Status,
                        serverNow
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

    public class ExtendSessionCommandHandler : ICommandHandler<ExtendSessionCommand, SessionExtensionResponseDto>
    {
        private readonly IRepository<Session> _sessionRepository;
        private readonly IRepository<SessionExtension> _extensionRepository;
        private readonly IRepository<GamerAccount> _accountRepository;
        private readonly Sayra.Backend.Application.Pricing.IRateSnapshotService _rateSnapshotService;
        private readonly Sayra.Backend.Application.Financial.IFinancialTransactionService _financialTransactionService;
        private readonly IRepository<AuditEvent> _auditEventRepository;
        private readonly IUnitOfWork _unitOfWork;

        public ExtendSessionCommandHandler(
            IRepository<Session> sessionRepository,
            IRepository<SessionExtension> extensionRepository,
            IRepository<GamerAccount> accountRepository,
            Sayra.Backend.Application.Pricing.IRateSnapshotService rateSnapshotService,
            Sayra.Backend.Application.Financial.IFinancialTransactionService financialTransactionService,
            IRepository<AuditEvent> auditEventRepository,
            IUnitOfWork unitOfWork)
        {
            _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
            _extensionRepository = extensionRepository ?? throw new ArgumentNullException(nameof(extensionRepository));
            _accountRepository = accountRepository ?? throw new ArgumentNullException(nameof(accountRepository));
            _rateSnapshotService = rateSnapshotService ?? throw new ArgumentNullException(nameof(rateSnapshotService));
            _financialTransactionService = financialTransactionService ?? throw new ArgumentNullException(nameof(financialTransactionService));
            _auditEventRepository = auditEventRepository ?? throw new ArgumentNullException(nameof(auditEventRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        }

        public async Task<Result<SessionExtensionResponseDto>> HandleAsync(ExtendSessionCommand command, CancellationToken cancellationToken = default)
        {
            try
            {
                var validator = new ExtendSessionCommandValidator();
                var validationResult = await validator.ValidateAsync(command, cancellationToken);
                if (!validationResult.IsValid)
                {
                    var firstError = validationResult.Errors.First();
                    return Result<SessionExtensionResponseDto>.Failure(firstError.ErrorCode ?? "VALIDATION_FAILED", firstError.ErrorMessage);
                }

                var idempotencyKey = string.IsNullOrWhiteSpace(command.IdempotencyKey)
                    ? $"EXT-{command.SessionId:N}-{command.AdditionalMinutes}"
                    : command.IdempotencyKey.Trim();

                // Idempotency check: Return existing extension if present
                var existingExtension = await _extensionRepository.FirstOrDefaultAsync(
                    e => e.IdempotencyKey == idempotencyKey,
                    track: false,
                    cancellationToken);

                if (existingExtension != null)
                {
                    return Result<SessionExtensionResponseDto>.Success(MapToDto(existingExtension));
                }

                var session = await _sessionRepository.GetByIdAsync(command.SessionId, track: false, cancellationToken);
                if (session == null)
                {
                    return Result<SessionExtensionResponseDto>.Failure("NOT_FOUND", $"Session with ID '{command.SessionId}' not found.");
                }

                if (session.Status != "ACTIVE" && session.Status != "PAUSED")
                {
                    return Result<SessionExtensionResponseDto>.Failure("INVALID_SESSION_STATUS", $"Cannot extend session in status '{session.Status}'.");
                }

                var snapshot = await _rateSnapshotService.GetSnapshotBySessionIdAsync(session.Id, cancellationToken);
                decimal hourlyRate = snapshot?.RateAmount ?? 10.00m;
                string currency = snapshot?.Currency ?? "SAY";

                // Cost calculation = (Rate per hour / 60) * AdditionalMinutes rounded to 4 decimal places
                decimal cost = Math.Round((hourlyRate / 60m) * command.AdditionalMinutes, 4);

                var gamerAccount = await _accountRepository.FirstOrDefaultAsync(
                    a => a.GamerEntityId == session.GamerId,
                    track: false,
                    cancellationToken);

                if (gamerAccount == null)
                {
                    return Result<SessionExtensionResponseDto>.Failure("ACCOUNT_NOT_FOUND", $"Financial account for Gamer '{session.GamerId}' not found.");
                }

                Guid? txId = null;
                if (cost > 0)
                {
                    var processTxDto = new ProcessTransactionRequestDto
                    {
                        GamerAccountId = gamerAccount.Id,
                        OperationType = "SESSION_EXTENSION",
                        Amount = cost,
                        Currency = currency,
                        IdempotencyKey = $"TX-{idempotencyKey}",
                        ReferenceId = session.Id.ToString(),
                        Description = $"Session extension of {command.AdditionalMinutes} minutes for Session {session.Id}"
                    };

                    var txResult = await _financialTransactionService.ProcessTransactionAsync(processTxDto, cancellationToken);
                    if (!txResult.IsSuccess)
                    {
                        return Result<SessionExtensionResponseDto>.Failure(txResult.ErrorCode ?? "FINANCIAL_TRANSACTION_FAILED", txResult.ErrorMessage);
                    }
                    txId = txResult.Value!.Id;
                }

                var serverNow = DateTime.UtcNow;
                var extension = new SessionExtension
                {
                    SessionId = session.Id,
                    ExtendedDuration = TimeSpan.FromMinutes(command.AdditionalMinutes),
                    Cost = cost,
                    Currency = currency,
                    IdempotencyKey = idempotencyKey,
                    FinancialTransactionId = txId,
                    CreatedAt = serverNow
                };
                extension.NormalizeAndValidate();

                await _extensionRepository.AddAsync(extension, cancellationToken);

                var auditEvent = new AuditEvent
                {
                    EventId = Guid.NewGuid(),
                    EventType = nameof(SessionExtended),
                    EventVersion = 1,
                    Timestamp = serverNow,
                    Payload = JsonSerializer.Serialize(new SessionExtended(
                        session.Id,
                        session.GamerId,
                        session.WorkstationId,
                        TimeSpan.FromMinutes(command.AdditionalMinutes),
                        cost,
                        serverNow
                    ))
                };
                await _auditEventRepository.AddAsync(auditEvent, cancellationToken);

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return Result<SessionExtensionResponseDto>.Success(MapToDto(extension));
            }
            catch (InvalidDomainException domainEx)
            {
                return Result<SessionExtensionResponseDto>.Failure(domainEx.ErrorCode, domainEx.Message);
            }
            catch (Exception ex)
            {
                return Result<SessionExtensionResponseDto>.Failure("EXTEND_SESSION_FAILED", ex.Message);
            }
        }

        private static SessionExtensionResponseDto MapToDto(SessionExtension e)
        {
            return new SessionExtensionResponseDto
            {
                SessionExtensionId = e.SessionExtensionId,
                SessionId = e.SessionId,
                ExtendedDuration = e.ExtendedDuration,
                Cost = e.Cost,
                Currency = e.Currency,
                IdempotencyKey = e.IdempotencyKey,
                FinancialTransactionId = e.FinancialTransactionId,
                CreatedAt = e.CreatedAt
            };
        }
    }

    public class TerminateSessionCommandHandler : ICommandHandler<TerminateSessionCommand, SessionResponseDto>
    {
        private readonly IRepository<Session> _sessionRepository;
        private readonly IRepository<SessionSegment> _segmentRepository;
        private readonly IRepository<AuditEvent> _auditEventRepository;
        private readonly IUnitOfWork _unitOfWork;

        public TerminateSessionCommandHandler(
            IRepository<Session> sessionRepository,
            IRepository<SessionSegment> segmentRepository,
            IRepository<AuditEvent> auditEventRepository,
            IUnitOfWork unitOfWork)
        {
            _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
            _segmentRepository = segmentRepository ?? throw new ArgumentNullException(nameof(segmentRepository));
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

                var serverNow = DateTime.UtcNow;

                var openSegment = await _segmentRepository.FirstOrDefaultAsync(
                    s => s.SessionId == session.Id && s.EndedAtUtc == null,
                    track: true,
                    cancellationToken);

                if (openSegment != null)
                {
                    openSegment.EndedAtUtc = serverNow;
                    _segmentRepository.Update(openSegment);
                }

                session.TransitionTo("TERMINATED");
                _sessionRepository.Update(session);

                var auditEvent = new AuditEvent
                {
                    EventId = Guid.NewGuid(),
                    EventType = nameof(SessionTerminated),
                    EventVersion = 1,
                    Timestamp = serverNow,
                    Payload = JsonSerializer.Serialize(new SessionTerminated(
                        session.Id,
                        session.GamerId,
                        session.WorkstationId,
                        session.Status,
                        command.Reason ?? "Terminated by administrator",
                        serverNow
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

    public class GetSessionCurrentStateQueryHandler : IQueryHandler<GetSessionCurrentStateQuery, SessionResponseDto>
    {
        private readonly IRepository<Session> _sessionRepository;

        public GetSessionCurrentStateQueryHandler(IRepository<Session> sessionRepository)
        {
            _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
        }

        public async Task<Result<SessionResponseDto>> HandleAsync(GetSessionCurrentStateQuery query, CancellationToken cancellationToken = default)
        {
            var validator = new GetSessionCurrentStateQueryValidator();
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

    public class GetSessionTimingQueryHandler : IQueryHandler<GetSessionTimingQuery, SessionTimingResponseDto>
    {
        private readonly IRepository<Session> _sessionRepository;
        private readonly IRepository<SessionSegment> _segmentRepository;
        private readonly IRepository<Reservation> _reservationRepository;
        private readonly ISessionTimeCalculator _timeCalculator;

        public GetSessionTimingQueryHandler(
            IRepository<Session> sessionRepository,
            IRepository<SessionSegment> segmentRepository,
            IRepository<Reservation> reservationRepository,
            ISessionTimeCalculator timeCalculator)
        {
            _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
            _segmentRepository = segmentRepository ?? throw new ArgumentNullException(nameof(segmentRepository));
            _reservationRepository = reservationRepository ?? throw new ArgumentNullException(nameof(reservationRepository));
            _timeCalculator = timeCalculator ?? throw new ArgumentNullException(nameof(timeCalculator));
        }

        public async Task<Result<SessionTimingResponseDto>> HandleAsync(GetSessionTimingQuery query, CancellationToken cancellationToken = default)
        {
            var validator = new GetSessionTimingQueryValidator();
            var validationResult = await validator.ValidateAsync(query, cancellationToken);
            if (!validationResult.IsValid)
            {
                var firstError = validationResult.Errors.First();
                return Result<SessionTimingResponseDto>.Failure(firstError.ErrorCode ?? "VALIDATION_FAILED", firstError.ErrorMessage);
            }

            var session = await _sessionRepository.GetByIdAsync(query.SessionId, track: false, cancellationToken);
            if (session == null)
            {
                return Result<SessionTimingResponseDto>.Failure("NOT_FOUND", $"Session with ID '{query.SessionId}' not found.");
            }

            var sessionSegments = await _segmentRepository.FindAsync(s => s.SessionId == query.SessionId, track: false, cancellationToken);

            TimeSpan? allocatedDuration = null;
            if (session.ReservationId.HasValue && session.ReservationId.Value != Guid.Empty)
            {
                var reservation = await _reservationRepository.GetByIdAsync(session.ReservationId.Value, track: false, cancellationToken);
                if (reservation != null && reservation.EndTimeUtc > reservation.StartTimeUtc)
                {
                    allocatedDuration = reservation.EndTimeUtc - reservation.StartTimeUtc;
                }
            }

            var serverNow = DateTime.UtcNow;
            var snapshot = _timeCalculator.CalculateTiming(session, sessionSegments, serverNow, allocatedDuration);

            var dto = new SessionTimingResponseDto
            {
                SessionId = snapshot.SessionId,
                CurrentServerTimeUtc = snapshot.CurrentServerTimeUtc,
                StartedAtUtc = snapshot.StartedAtUtc,
                ConsumedDuration = snapshot.ConsumedDuration,
                PausedDuration = snapshot.PausedDuration,
                RemainingDuration = snapshot.RemainingDuration,
                ExpirationTimeUtc = snapshot.ExpirationTimeUtc
            };

            return Result<SessionTimingResponseDto>.Success(dto);
        }
    }

    public class GetSessionDurationQueryHandler : IQueryHandler<GetSessionDurationQuery, TimeSpan>
    {
        private readonly IRepository<Session> _sessionRepository;
        private readonly IRepository<SessionSegment> _segmentRepository;
        private readonly ISessionTimeCalculator _timeCalculator;

        public GetSessionDurationQueryHandler(
            IRepository<Session> sessionRepository,
            IRepository<SessionSegment> segmentRepository,
            ISessionTimeCalculator timeCalculator)
        {
            _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
            _segmentRepository = segmentRepository ?? throw new ArgumentNullException(nameof(segmentRepository));
            _timeCalculator = timeCalculator ?? throw new ArgumentNullException(nameof(timeCalculator));
        }

        public async Task<Result<TimeSpan>> HandleAsync(GetSessionDurationQuery query, CancellationToken cancellationToken = default)
        {
            var validator = new GetSessionDurationQueryValidator();
            var validationResult = await validator.ValidateAsync(query, cancellationToken);
            if (!validationResult.IsValid)
            {
                var firstError = validationResult.Errors.First();
                return Result<TimeSpan>.Failure(firstError.ErrorCode ?? "VALIDATION_FAILED", firstError.ErrorMessage);
            }

            var session = await _sessionRepository.GetByIdAsync(query.SessionId, track: false, cancellationToken);
            if (session == null)
            {
                return Result<TimeSpan>.Failure("NOT_FOUND", $"Session with ID '{query.SessionId}' not found.");
            }

            var sessionSegments = await _segmentRepository.FindAsync(s => s.SessionId == query.SessionId, track: false, cancellationToken);

            var snapshot = _timeCalculator.CalculateTiming(session, sessionSegments, DateTime.UtcNow, null);
            return Result<TimeSpan>.Success(snapshot.ConsumedDuration);
        }
    }

    public class GetSessionRemainingTimeQueryHandler : IQueryHandler<GetSessionRemainingTimeQuery, TimeSpan?>
    {
        private readonly IRepository<Session> _sessionRepository;
        private readonly IRepository<SessionSegment> _segmentRepository;
        private readonly IRepository<Reservation> _reservationRepository;
        private readonly ISessionTimeCalculator _timeCalculator;

        public GetSessionRemainingTimeQueryHandler(
            IRepository<Session> sessionRepository,
            IRepository<SessionSegment> segmentRepository,
            IRepository<Reservation> reservationRepository,
            ISessionTimeCalculator timeCalculator)
        {
            _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
            _segmentRepository = segmentRepository ?? throw new ArgumentNullException(nameof(segmentRepository));
            _reservationRepository = reservationRepository ?? throw new ArgumentNullException(nameof(reservationRepository));
            _timeCalculator = timeCalculator ?? throw new ArgumentNullException(nameof(timeCalculator));
        }

        public async Task<Result<TimeSpan?>> HandleAsync(GetSessionRemainingTimeQuery query, CancellationToken cancellationToken = default)
        {
            var validator = new GetSessionRemainingTimeQueryValidator();
            var validationResult = await validator.ValidateAsync(query, cancellationToken);
            if (!validationResult.IsValid)
            {
                var firstError = validationResult.Errors.First();
                return Result<TimeSpan?>.Failure(firstError.ErrorCode ?? "VALIDATION_FAILED", firstError.ErrorMessage);
            }

            var session = await _sessionRepository.GetByIdAsync(query.SessionId, track: false, cancellationToken);
            if (session == null)
            {
                return Result<TimeSpan?>.Failure("NOT_FOUND", $"Session with ID '{query.SessionId}' not found.");
            }

            var sessionSegments = await _segmentRepository.FindAsync(s => s.SessionId == query.SessionId, track: false, cancellationToken);

            TimeSpan? allocatedDuration = null;
            if (session.ReservationId.HasValue && session.ReservationId.Value != Guid.Empty)
            {
                var reservation = await _reservationRepository.GetByIdAsync(session.ReservationId.Value, track: false, cancellationToken);
                if (reservation != null && reservation.EndTimeUtc > reservation.StartTimeUtc)
                {
                    allocatedDuration = reservation.EndTimeUtc - reservation.StartTimeUtc;
                }
            }

            var snapshot = _timeCalculator.CalculateTiming(session, sessionSegments, DateTime.UtcNow, allocatedDuration);
            return Result<TimeSpan?>.Success(snapshot.RemainingDuration);
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
