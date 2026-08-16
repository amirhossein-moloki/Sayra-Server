using System;
using System.Collections.Generic;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Contracts;

namespace Sayra.Backend.Application.Pricing
{
    public record CreatePricingPlanCommand(
        Guid SiteId,
        string Name,
        string Currency = "SAY") : ICommand<PricingPlanResponseDto>;

    public record CreatePricingRuleCommand(
        Guid PricingPlanId,
        string Name,
        decimal RateAmount,
        string Currency = "SAY",
        int Priority = 1,
        Guid? WorkstationId = null,
        Guid? ZoneId = null,
        string? GamerType = null,
        DayOfWeek? DayOfWeek = null,
        TimeSpan? StartTime = null,
        TimeSpan? EndTime = null,
        bool? IsPeak = null) : ICommand<PricingRuleResponseDto>;

    public record ActivatePricingPlanCommand(
        Guid PricingPlanId) : ICommand<PricingPlanResponseDto>;

    public record DeactivatePricingPlanCommand(
        Guid PricingPlanId) : ICommand<PricingPlanResponseDto>;

    public record GetPricingPlanQuery(
        Guid PricingPlanId) : IQuery<PricingPlanResponseDto>;

    public record GetPricingRulesQuery(
        Guid PricingPlanId) : IQuery<List<PricingRuleResponseDto>>;

    public record ResolveRateQuery(
        Guid SiteId,
        Guid? ZoneId = null,
        Guid? WorkstationId = null,
        Guid? GamerId = null,
        string? GamerType = null,
        DateTime? Timestamp = null) : IQuery<ResolvedRateResponseDto>;
}
