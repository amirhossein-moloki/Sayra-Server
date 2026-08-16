using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Exceptions;

namespace Sayra.Backend.Application.Pricing
{
    public class RateSnapshotService : IRateSnapshotService
    {
        private readonly IRepository<RateSnapshot> _snapshotRepository;
        private readonly IUnitOfWork _unitOfWork;

        public RateSnapshotService(
            IRepository<RateSnapshot> snapshotRepository,
            IUnitOfWork unitOfWork)
        {
            _snapshotRepository = snapshotRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<RateSnapshotResponseDto> CreateSnapshotAsync(
            Guid sessionId,
            Guid pricingPlanId,
            Guid? pricingRuleId,
            decimal rateAmount,
            string currency,
            string ruleReference,
            DateTime appliedAtUtc,
            CancellationToken cancellationToken = default)
        {
            if (sessionId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_SESSION_ID", "SessionId is required for RateSnapshot.");
            }

            var existingSnapshots = await _snapshotRepository.FindAsync(
                s => s.SessionId == sessionId,
                track: false,
                cancellationToken: cancellationToken);

            var existing = existingSnapshots.FirstOrDefault();
            if (existing != null)
            {
                // Rate snapshots are immutable!
                return MapToDto(existing);
            }

            var snapshot = new RateSnapshot
            {
                SessionId = sessionId,
                PricingPlanId = pricingPlanId,
                PricingRuleId = pricingRuleId,
                RateAmount = rateAmount,
                Currency = string.IsNullOrWhiteSpace(currency) ? "SAY" : currency.Trim().ToUpperInvariant(),
                RuleReference = string.IsNullOrWhiteSpace(ruleReference) ? "Default Rate" : ruleReference.Trim(),
                AppliedAtUtc = appliedAtUtc.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(appliedAtUtc, DateTimeKind.Utc)
                    : appliedAtUtc.ToUniversalTime(),
                CreatedAt = DateTime.UtcNow
            };

            snapshot.NormalizeAndValidate();

            await _snapshotRepository.AddAsync(snapshot, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return MapToDto(snapshot);
        }

        public async Task<RateSnapshotResponseDto?> GetSnapshotBySessionIdAsync(
            Guid sessionId,
            CancellationToken cancellationToken = default)
        {
            if (sessionId == Guid.Empty)
            {
                return null;
            }

            var snapshots = await _snapshotRepository.FindAsync(
                s => s.SessionId == sessionId,
                track: false,
                cancellationToken: cancellationToken);

            var snapshot = snapshots.FirstOrDefault();
            return snapshot != null ? MapToDto(snapshot) : null;
        }

        private static RateSnapshotResponseDto MapToDto(RateSnapshot snapshot)
        {
            return new RateSnapshotResponseDto
            {
                RateSnapshotId = snapshot.RateSnapshotId,
                SessionId = snapshot.SessionId,
                PricingPlanId = snapshot.PricingPlanId,
                PricingRuleId = snapshot.PricingRuleId,
                RateAmount = snapshot.RateAmount,
                Currency = snapshot.Currency,
                AppliedAtUtc = snapshot.AppliedAtUtc,
                RuleReference = snapshot.RuleReference,
                CreatedAt = snapshot.CreatedAt
            };
        }
    }
}
