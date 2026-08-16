using System;

namespace Sayra.Backend.Domain.Events
{
    public record PricingPlanCreated(
        Guid PricingPlanId,
        Guid SiteId,
        string Name,
        string Currency,
        DateTime OccurredOn,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), OccurredOn, CorrelationId);

    public record PricingRuleCreated(
        Guid PricingRuleId,
        Guid PricingPlanId,
        string Name,
        decimal RateAmount,
        string Currency,
        int Priority,
        DateTime OccurredOn,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), OccurredOn, CorrelationId);

    public record PricingPlanActivated(
        Guid PricingPlanId,
        Guid SiteId,
        DateTime OccurredOn,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), OccurredOn, CorrelationId);

    public record PricingPlanDeactivated(
        Guid PricingPlanId,
        Guid SiteId,
        DateTime OccurredOn,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), OccurredOn, CorrelationId);

    public record RateResolved(
        Guid SiteId,
        Guid PricingPlanId,
        Guid? PricingRuleId,
        decimal RateAmount,
        string Currency,
        DateTime ResolvedAtUtc,
        DateTime OccurredOn,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), OccurredOn, CorrelationId);

    public record RateSnapshotCreated(
        Guid RateSnapshotId,
        Guid SessionId,
        Guid PricingPlanId,
        Guid? PricingRuleId,
        decimal RateAmount,
        string Currency,
        DateTime AppliedAtUtc,
        DateTime OccurredOn,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), OccurredOn, CorrelationId);
}
