using System;
using Sayra.Backend.Domain.Exceptions;

namespace Sayra.Backend.Domain
{
    public class PricingRule : BaseEntity
    {
        public Guid PricingRuleId
        {
            get => Id;
            set => Id = value;
        }

        public Guid PricingPlanId { get; set; }
        public string Name { get; set; } = string.Empty;

        public decimal RateAmount { get; set; }
        public string Currency { get; set; } = "SAY";
        public int Priority { get; set; } = 1;

        // Rule dimensions
        public Guid? WorkstationId { get; set; }
        public Guid? ZoneId { get; set; }
        public string? GamerType { get; set; }
        public DayOfWeek? DayOfWeek { get; set; }
        public TimeSpan? StartTime { get; set; }
        public TimeSpan? EndTime { get; set; }
        public bool? IsPeak { get; set; }

        public void NormalizeAndValidate()
        {
            if (PricingPlanId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_PRICING_PLAN_ID", "PricingPlanId is required for PricingRule.");
            }

            if (string.IsNullOrWhiteSpace(Name))
            {
                throw new InvalidDomainException("INVALID_RULE_NAME", "Pricing rule name is required.");
            }

            Name = Name.Trim();

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

            if (Priority <= 0)
            {
                throw new InvalidDomainException("INVALID_PRIORITY", "Priority must be greater than zero.");
            }

            if (!string.IsNullOrWhiteSpace(GamerType))
            {
                GamerType = GamerType.Trim().ToUpperInvariant();
            }
            else
            {
                GamerType = null;
            }

            if (StartTime.HasValue && EndTime.HasValue)
            {
                if (StartTime.Value < TimeSpan.Zero || StartTime.Value >= TimeSpan.FromDays(1))
                {
                    throw new InvalidDomainException("INVALID_TIME_RANGE", "StartTime must be between 00:00:00 and 23:59:59.");
                }
                if (EndTime.Value < TimeSpan.Zero || EndTime.Value >= TimeSpan.FromDays(1))
                {
                    throw new InvalidDomainException("INVALID_TIME_RANGE", "EndTime must be between 00:00:00 and 23:59:59.");
                }
            }
        }

        public bool Matches(
            Guid? siteId,
            Guid? zoneId,
            Guid? workstationId,
            string? gamerType,
            DateTime timestampUtc,
            bool? isPeakInput = null)
        {
            if (WorkstationId.HasValue && WorkstationId.Value != workstationId)
            {
                return false;
            }

            if (ZoneId.HasValue && ZoneId.Value != zoneId)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(GamerType) && !string.Equals(GamerType, gamerType, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (DayOfWeek.HasValue && timestampUtc.DayOfWeek != DayOfWeek.Value)
            {
                return false;
            }

            if (StartTime.HasValue && EndTime.HasValue)
            {
                var timeOfDay = timestampUtc.TimeOfDay;
                if (StartTime.Value <= EndTime.Value)
                {
                    if (timeOfDay < StartTime.Value || timeOfDay >= EndTime.Value)
                    {
                        return false;
                    }
                }
                else
                {
                    // Overnight time range (e.g., 22:00 to 06:00)
                    if (timeOfDay < StartTime.Value && timeOfDay >= EndTime.Value)
                    {
                        return false;
                    }
                }
            }

            if (IsPeak.HasValue && (!isPeakInput.HasValue || IsPeak.Value != isPeakInput.Value))
            {
                return false;
            }

            return true;
        }
    }
}
