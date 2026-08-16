using System;
using System.Collections.Generic;

namespace Sayra.Backend.Contracts
{
    public class CreatePricingPlanRequestDto
    {
        public Guid SiteId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Currency { get; set; } = "SAY";
    }

    public class CreatePricingRuleRequestDto
    {
        public string Name { get; set; } = string.Empty;
        public decimal RateAmount { get; set; }
        public string Currency { get; set; } = "SAY";
        public int Priority { get; set; } = 1;

        public Guid? WorkstationId { get; set; }
        public Guid? ZoneId { get; set; }
        public string? GamerType { get; set; }
        public DayOfWeek? DayOfWeek { get; set; }
        public TimeSpan? StartTime { get; set; }
        public TimeSpan? EndTime { get; set; }
        public bool? IsPeak { get; set; }
    }

    public class PricingPlanResponseDto
    {
        public Guid PricingPlanId { get; set; }
        public Guid SiteId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Currency { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public List<PricingRuleResponseDto> Rules { get; set; } = new();
    }

    public class PricingRuleResponseDto
    {
        public Guid PricingRuleId { get; set; }
        public Guid PricingPlanId { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal RateAmount { get; set; }
        public string Currency { get; set; } = string.Empty;
        public int Priority { get; set; }

        public Guid? WorkstationId { get; set; }
        public Guid? ZoneId { get; set; }
        public string? GamerType { get; set; }
        public DayOfWeek? DayOfWeek { get; set; }
        public TimeSpan? StartTime { get; set; }
        public TimeSpan? EndTime { get; set; }
        public bool? IsPeak { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class RateSnapshotResponseDto
    {
        public Guid RateSnapshotId { get; set; }
        public Guid SessionId { get; set; }
        public Guid PricingPlanId { get; set; }
        public Guid? PricingRuleId { get; set; }
        public decimal RateAmount { get; set; }
        public string Currency { get; set; } = string.Empty;
        public DateTime AppliedAtUtc { get; set; }
        public string RuleReference { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public class ResolveRateRequestDto
    {
        public Guid SiteId { get; set; }
        public Guid? ZoneId { get; set; }
        public Guid? WorkstationId { get; set; }
        public Guid? GamerId { get; set; }
        public string? GamerType { get; set; }
        public DateTime? Timestamp { get; set; }
    }

    public class ResolvedRateResponseDto
    {
        public Guid PricingPlanId { get; set; }
        public Guid? PricingRuleId { get; set; }
        public decimal RateAmount { get; set; }
        public string Currency { get; set; } = string.Empty;
        public int Priority { get; set; }
        public string RuleReference { get; set; } = string.Empty;
        public DateTime ResolvedAtUtc { get; set; }
    }
}
