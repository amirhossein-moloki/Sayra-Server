using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Sessions;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Shared;

namespace Sayra.Backend.Application.Billing
{
    public class CalculateSessionBillingCommandHandler : ICommandHandler<CalculateSessionBillingCommand, BillingResultResponseDto>
    {
        private readonly IRepository<Session> _sessionRepository;
        private readonly IRepository<SessionSegment> _segmentRepository;
        private readonly IRepository<RateSnapshot> _rateSnapshotRepository;
        private readonly IRepository<BillingResult> _billingResultRepository;
        private readonly ISessionTimeCalculator _sessionTimeCalculator;
        private readonly IBillingCalculator _billingCalculator;
        private readonly IUnitOfWork _unitOfWork;

        public CalculateSessionBillingCommandHandler(
            IRepository<Session> sessionRepository,
            IRepository<SessionSegment> segmentRepository,
            IRepository<RateSnapshot> rateSnapshotRepository,
            IRepository<BillingResult> billingResultRepository,
            ISessionTimeCalculator sessionTimeCalculator,
            IBillingCalculator billingCalculator,
            IUnitOfWork unitOfWork)
        {
            _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
            _segmentRepository = segmentRepository ?? throw new ArgumentNullException(nameof(segmentRepository));
            _rateSnapshotRepository = rateSnapshotRepository ?? throw new ArgumentNullException(nameof(rateSnapshotRepository));
            _billingResultRepository = billingResultRepository ?? throw new ArgumentNullException(nameof(billingResultRepository));
            _sessionTimeCalculator = sessionTimeCalculator ?? throw new ArgumentNullException(nameof(sessionTimeCalculator));
            _billingCalculator = billingCalculator ?? throw new ArgumentNullException(nameof(billingCalculator));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        }

        public async Task<Result<BillingResultResponseDto>> HandleAsync(
            CalculateSessionBillingCommand command,
            CancellationToken cancellationToken = default)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));

            var sessionList = await _sessionRepository.FindAsync(s => s.Id == command.SessionId, track: false, cancellationToken: cancellationToken);
            var session = sessionList.FirstOrDefault();
            if (session == null)
            {
                return Result<BillingResultResponseDto>.Failure("SESSION_NOT_FOUND", $"Session '{command.SessionId}' was not found.");
            }

            var snapshotList = await _rateSnapshotRepository.FindAsync(r => r.SessionId == command.SessionId, track: false, cancellationToken: cancellationToken);
            var rateSnapshot = snapshotList.FirstOrDefault();
            if (rateSnapshot == null)
            {
                return Result<BillingResultResponseDto>.Failure("RATE_SNAPSHOT_NOT_FOUND", $"No rate snapshot found for session '{command.SessionId}'. Billing cannot proceed.");
            }

            var segments = await _segmentRepository.FindAsync(s => s.SessionId == command.SessionId, track: false, cancellationToken: cancellationToken);

            var timing = _sessionTimeCalculator.CalculateTiming(session, segments, DateTime.UtcNow);

            var currency = rateSnapshot.Currency ?? "SAY";
            Money? discountMoney = command.DiscountAmount.HasValue ? new Money(command.DiscountAmount.Value, currency) : null;
            Money? adjustmentMoney = command.AdjustmentAmount.HasValue ? new Money(command.AdjustmentAmount.Value, currency) : null;

            var billingResult = _billingCalculator.CalculateBilling(
                session,
                timing,
                rateSnapshot,
                discountMoney,
                adjustmentMoney,
                command.CorrelationId);

            await _billingResultRepository.AddAsync(billingResult, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<BillingResultResponseDto>.Success(MapToDto(billingResult));
        }

        internal static BillingResultResponseDto MapToDto(BillingResult result)
        {
            return new BillingResultResponseDto
            {
                BillingResultId = result.BillingResultId,
                SessionId = result.SessionId,
                ConsumedDuration = result.ConsumedDuration,
                RateSnapshotId = result.RateSnapshotId,
                Subtotal = result.Subtotal?.Amount ?? 0m,
                DiscountAmount = result.DiscountAmount?.Amount ?? 0m,
                AdjustmentAmount = result.AdjustmentAmount?.Amount ?? 0m,
                FinalAmount = result.FinalAmount?.Amount ?? 0m,
                Currency = result.Currency,
                CalculatedAtUtc = result.CalculatedAtUtc,
                CorrelationId = result.CorrelationId,
                CreatedAt = result.CreatedAt
            };
        }
    }

    public class GetBillingResultQueryHandler : IQueryHandler<GetBillingResultQuery, BillingResultResponseDto>
    {
        private readonly IRepository<BillingResult> _billingResultRepository;

        public GetBillingResultQueryHandler(IRepository<BillingResult> billingResultRepository)
        {
            _billingResultRepository = billingResultRepository ?? throw new ArgumentNullException(nameof(billingResultRepository));
        }

        public async Task<Result<BillingResultResponseDto>> HandleAsync(
            GetBillingResultQuery query,
            CancellationToken cancellationToken = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));

            var results = await _billingResultRepository.FindAsync(b => b.Id == query.BillingResultId, track: false, cancellationToken: cancellationToken);
            var billing = results.FirstOrDefault();

            if (billing == null)
            {
                return Result<BillingResultResponseDto>.Failure("NOT_FOUND", $"Billing result '{query.BillingResultId}' was not found.");
            }

            return Result<BillingResultResponseDto>.Success(CalculateSessionBillingCommandHandler.MapToDto(billing));
        }
    }

    public class GetSessionBillingHistoryQueryHandler : IQueryHandler<GetSessionBillingHistoryQuery, List<BillingResultResponseDto>>
    {
        private readonly IRepository<BillingResult> _billingResultRepository;

        public GetSessionBillingHistoryQueryHandler(IRepository<BillingResult> billingResultRepository)
        {
            _billingResultRepository = billingResultRepository ?? throw new ArgumentNullException(nameof(billingResultRepository));
        }

        public async Task<Result<List<BillingResultResponseDto>>> HandleAsync(
            GetSessionBillingHistoryQuery query,
            CancellationToken cancellationToken = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));

            var results = await _billingResultRepository.FindAsync(b => b.SessionId == query.SessionId, track: false, cancellationToken: cancellationToken);

            var dtos = results
                .OrderByDescending(b => b.CalculatedAtUtc)
                .ThenByDescending(b => b.CreatedAt)
                .Select(CalculateSessionBillingCommandHandler.MapToDto)
                .ToList();

            return Result<List<BillingResultResponseDto>>.Success(dtos);
        }
    }

    public class GetLatestSessionBillingQueryHandler : IQueryHandler<GetLatestSessionBillingQuery, BillingResultResponseDto>
    {
        private readonly IRepository<BillingResult> _billingResultRepository;

        public GetLatestSessionBillingQueryHandler(IRepository<BillingResult> billingResultRepository)
        {
            _billingResultRepository = billingResultRepository ?? throw new ArgumentNullException(nameof(billingResultRepository));
        }

        public async Task<Result<BillingResultResponseDto>> HandleAsync(
            GetLatestSessionBillingQuery query,
            CancellationToken cancellationToken = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));

            var results = await _billingResultRepository.FindAsync(b => b.SessionId == query.SessionId, track: false, cancellationToken: cancellationToken);
            var latest = results
                .OrderByDescending(b => b.CalculatedAtUtc)
                .ThenByDescending(b => b.CreatedAt)
                .FirstOrDefault();

            if (latest == null)
            {
                return Result<BillingResultResponseDto>.Failure("NOT_FOUND", $"No billing result found for session '{query.SessionId}'.");
            }

            return Result<BillingResultResponseDto>.Success(CalculateSessionBillingCommandHandler.MapToDto(latest));
        }
    }
}
