using System;

namespace Sayra.Backend.Domain.Events
{
    public record BillingCalculatedEvent(
        Guid BillingResultId,
        Guid SessionId,
        Guid RateSnapshotId,
        decimal Subtotal,
        decimal DiscountAmount,
        decimal AdjustmentAmount,
        decimal FinalAmount,
        string Currency,
        DateTime CalculatedAtUtc,
        DateTime OccurredOnUtc,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), OccurredOnUtc, CorrelationId);

    public record BillingCalculationFailedEvent(
        Guid SessionId,
        string ErrorCode,
        string ErrorMessage,
        DateTime FailedAtUtc,
        DateTime OccurredOnUtc,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), OccurredOnUtc, CorrelationId);
}
