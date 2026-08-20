using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Domain.Events;
using Sayra.Backend.Domain.Exceptions;
using Sayra.Backend.Shared;

namespace Sayra.Backend.Application.Sessions
{
    public interface ISessionExpirationService
    {
        Task<Result<SessionResponseDto>> ExpireSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);
    }

    public class SessionExpirationService : ISessionExpirationService
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

        public SessionExpirationService(
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

        public async Task<Result<SessionResponseDto>> ExpireSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
        {
            if (sessionId == Guid.Empty)
            {
                return Result<SessionResponseDto>.Failure("INVALID_SESSION_ID", "SessionId cannot be empty.");
            }

            try
            {
                var session = await _sessionRepository.GetByIdAsync(sessionId, track: true, cancellationToken);
                if (session == null)
                {
                    return Result<SessionResponseDto>.Failure("NOT_FOUND", $"Session with ID '{sessionId}' not found.");
                }

                if (session.Status == "EXPIRED" || session.Status == "ENDED" || session.Status == "CANCELLED" || session.Status == "TERMINATED")
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
                                IdempotencyKey = $"TX-EXPIRE-{session.Id:N}",
                                ReferenceId = session.Id.ToString(),
                                Description = $"Expiration usage charge for Session {session.Id} (Total: {totalUsageCost}, Prepaid: {prepaidExtensionsCost})"
                            };

                            var txResult = await _financialTransactionService.ProcessTransactionAsync(txDto, cancellationToken);
                            if (!txResult.IsSuccess)
                            {
                                return Result<SessionResponseDto>.Failure(txResult.ErrorCode ?? "FINANCIAL_TRANSACTION_FAILED", txResult.ErrorMessage);
                            }
                        }
                    }

                    session.TransitionTo("EXPIRED");
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
                        EventType = nameof(SessionExpired),
                        EventVersion = 1,
                        Timestamp = serverNow,
                        Payload = JsonSerializer.Serialize(new SessionExpired(
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
                return Result<SessionResponseDto>.Failure("EXPIRE_SESSION_FAILED", ex.Message);
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
}
