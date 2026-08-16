using System;
using Sayra.Backend.Domain.Exceptions;

namespace Sayra.Backend.Domain
{
    public class RateSnapshot : BaseEntity
    {
        public Guid RateSnapshotId
        {
            get => Id;
            set => Id = value;
        }

        public Guid SessionId { get; set; }
        public Guid PricingPlanId { get; set; }
        public Guid? PricingRuleId { get; set; }

        public decimal RateAmount { get; set; }
        public string Currency { get; set; } = "SAY";
        public DateTime AppliedAtUtc { get; set; } = DateTime.UtcNow;
        public string RuleReference { get; set; } = string.Empty;

        public void NormalizeAndValidate()
        {
            if (SessionId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_SESSION_ID", "SessionId is required for RateSnapshot.");
            }

            if (PricingPlanId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_PRICING_PLAN_ID", "PricingPlanId is required for RateSnapshot.");
            }

            if (RateAmount < 0)
            {
                throw new InvalidDomainException("INVALID_RATE_AMOUNT", "Rate amount cannot be negative.");
            }

            RateAmount = Math.Round(RateAmount, 4, MidpointRounding.AwayFromZero);

            if (string.IsNullOrWhiteSpace(Currency))
            {
                Currency = "SAY";
            }
            else
            {
                Currency = Currency.Trim().ToUpperInvariant();
            }

            RuleReference = (RuleReference ?? string.Empty).Trim();

            AppliedAtUtc = AppliedAtUtc.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(AppliedAtUtc, DateTimeKind.Utc)
                : AppliedAtUtc.ToUniversalTime();
        }
    }
}
