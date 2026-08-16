using System;
using Sayra.Backend.Domain.Exceptions;
using Sayra.Backend.Shared;

namespace Sayra.Backend.Domain
{
    public class BillingResult : BaseEntity
    {
        public Guid BillingResultId
        {
            get => Id;
            set => Id = value;
        }

        public Guid SessionId { get; set; }
        public TimeSpan ConsumedDuration { get; set; }
        public Guid RateSnapshotId { get; set; }

        public Money Subtotal { get; set; } = Money.Zero();
        public Money DiscountAmount { get; set; } = Money.Zero();
        public Money AdjustmentAmount { get; set; } = Money.Zero();
        public Money FinalAmount { get; set; } = Money.Zero();

        public string Currency { get; set; } = "SAY";
        public DateTime CalculatedAtUtc { get; set; } = DateTime.UtcNow;
        public string? CorrelationId { get; set; }

        public void NormalizeAndValidate()
        {
            if (SessionId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_SESSION_ID", "SessionId is required for BillingResult.");
            }

            if (RateSnapshotId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_RATE_SNAPSHOT_ID", "RateSnapshotId is required for BillingResult.");
            }

            if (ConsumedDuration < TimeSpan.Zero)
            {
                throw new InvalidDomainException("INVALID_CONSUMED_DURATION", "Consumed duration cannot be negative.");
            }

            if (string.IsNullOrWhiteSpace(Currency))
            {
                Currency = "SAY";
            }
            else
            {
                Currency = Currency.Trim().ToUpperInvariant();
            }

            Subtotal ??= Money.Zero(Currency);
            DiscountAmount ??= Money.Zero(Currency);
            AdjustmentAmount ??= Money.Zero(Currency);

            if (Subtotal.Currency != Currency || DiscountAmount.Currency != Currency || AdjustmentAmount.Currency != Currency)
            {
                throw new InvalidDomainException("CURRENCY_MISMATCH", "Currency across Subtotal, DiscountAmount, and AdjustmentAmount must match.");
            }

            if (DiscountAmount.Amount < 0)
            {
                throw new InvalidDomainException("INVALID_DISCOUNT_AMOUNT", "Discount amount cannot be negative.");
            }

            // FinalAmount = Subtotal - Discount + Adjustment
            var calculatedFinal = Subtotal - DiscountAmount + AdjustmentAmount;
            if (calculatedFinal.Amount < 0)
            {
                calculatedFinal = Money.Zero(Currency);
            }

            FinalAmount = calculatedFinal;

            CalculatedAtUtc = CalculatedAtUtc.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(CalculatedAtUtc, DateTimeKind.Utc)
                : CalculatedAtUtc.ToUniversalTime();

            CorrelationId = CorrelationId?.Trim();
        }
    }
}
