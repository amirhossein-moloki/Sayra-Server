using System;

namespace Sayra.Backend.Application.Billing
{
    public interface IBillingCalculator
    {
        Domain.BillingResult CalculateBilling(
            Domain.Session session,
            Sessions.SessionTimingSnapshot timingSnapshot,
            Domain.RateSnapshot rateSnapshot,
            Shared.Money? discount = null,
            Shared.Money? adjustment = null,
            string? correlationId = null);
    }

    public class BillingCalculator : IBillingCalculator
    {
        public Domain.BillingResult CalculateBilling(
            Domain.Session session,
            Sessions.SessionTimingSnapshot timingSnapshot,
            Domain.RateSnapshot rateSnapshot,
            Shared.Money? discount = null,
            Shared.Money? adjustment = null,
            string? correlationId = null)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (timingSnapshot == null) throw new ArgumentNullException(nameof(timingSnapshot));
            if (rateSnapshot == null) throw new ArgumentNullException(nameof(rateSnapshot));

            if (session.Id != timingSnapshot.SessionId)
            {
                throw new Domain.Exceptions.InvalidDomainException("SESSION_MISMATCH", "SessionId on session and timing snapshot must match.");
            }

            if (session.Id != rateSnapshot.SessionId)
            {
                throw new Domain.Exceptions.InvalidDomainException("SESSION_MISMATCH", "SessionId on session and rate snapshot must match.");
            }

            var currency = rateSnapshot.Currency ?? "SAY";
            var consumedDuration = timingSnapshot.ConsumedDuration < TimeSpan.Zero ? TimeSpan.Zero : timingSnapshot.ConsumedDuration;

            // Hourly rate calculation: (ConsumedDuration.TotalMinutes / 60.0) * RateAmount
            // Calculated using decimal precision:
            decimal totalHours = (decimal)consumedDuration.TotalSeconds / 3600m;
            decimal rawSubtotalAmount = rateSnapshot.RateAmount * totalHours;

            var subtotal = new Shared.Money(rawSubtotalAmount, currency);
            var discountAmount = discount ?? Shared.Money.Zero(currency);
            var adjustmentAmount = adjustment ?? Shared.Money.Zero(currency);

            var billingResult = new Domain.BillingResult
            {
                BillingResultId = Guid.NewGuid(),
                SessionId = session.Id,
                ConsumedDuration = consumedDuration,
                RateSnapshotId = rateSnapshot.RateSnapshotId,
                Subtotal = subtotal,
                DiscountAmount = discountAmount,
                AdjustmentAmount = adjustmentAmount,
                Currency = currency,
                CalculatedAtUtc = timingSnapshot.CurrentServerTimeUtc,
                CorrelationId = correlationId,
                CreatedAt = DateTime.UtcNow
            };

            billingResult.NormalizeAndValidate();
            return billingResult;
        }
    }
}
