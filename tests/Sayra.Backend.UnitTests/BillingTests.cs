using System;
using Sayra.Backend.Application.Billing;
using Sayra.Backend.Application.Sessions;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Exceptions;
using Sayra.Backend.Shared;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class BillingTests
    {
        private readonly BillingCalculator _calculator;

        public BillingTests()
        {
            _calculator = new BillingCalculator();
        }

        [Fact]
        public void CalculateBilling_StandardHourlyRate_CalculatesCorrectSubtotal()
        {
            // Arrange: 90 minutes consumed duration @ 25,000 SAY / hour
            var sessionId = Guid.NewGuid();
            var session = new Session { SessionId = sessionId, Status = "ACTIVE" };
            var timing = new SessionTimingSnapshot
            {
                SessionId = sessionId,
                ConsumedDuration = TimeSpan.FromMinutes(90),
                CurrentServerTimeUtc = DateTime.UtcNow
            };
            var rateSnapshot = new RateSnapshot
            {
                RateSnapshotId = Guid.NewGuid(),
                SessionId = sessionId,
                PricingPlanId = Guid.NewGuid(),
                RateAmount = 25000m,
                Currency = "SAY"
            };

            // Act
            var result = _calculator.CalculateBilling(session, timing, rateSnapshot);

            // Assert
            // 90 mins = 1.5 hours * 25000 = 37500
            Assert.Equal(37500m, result.Subtotal.Amount);
            Assert.Equal(0m, result.DiscountAmount.Amount);
            Assert.Equal(0m, result.AdjustmentAmount.Amount);
            Assert.Equal(37500m, result.FinalAmount.Amount);
            Assert.Equal("SAY", result.Currency);
        }

        [Fact]
        public void CalculateBilling_WithDiscountAndAdjustment_CalculatesFinalAmountCorrectly()
        {
            // Arrange: 120 minutes @ 10,000 SAY/hr = 20,000 Subtotal
            // Discount = 2,000 SAY, Adjustment = 500 SAY => Final = 18,500 SAY
            var sessionId = Guid.NewGuid();
            var session = new Session { SessionId = sessionId, Status = "ENDED" };
            var timing = new SessionTimingSnapshot
            {
                SessionId = sessionId,
                ConsumedDuration = TimeSpan.FromHours(2),
                CurrentServerTimeUtc = DateTime.UtcNow
            };
            var rateSnapshot = new RateSnapshot
            {
                RateSnapshotId = Guid.NewGuid(),
                SessionId = sessionId,
                PricingPlanId = Guid.NewGuid(),
                RateAmount = 10000m,
                Currency = "SAY"
            };

            var discount = new Money(2000m, "SAY");
            var adjustment = new Money(500m, "SAY");

            // Act
            var result = _calculator.CalculateBilling(session, timing, rateSnapshot, discount, adjustment);

            // Assert
            Assert.Equal(20000m, result.Subtotal.Amount);
            Assert.Equal(2000m, result.DiscountAmount.Amount);
            Assert.Equal(500m, result.AdjustmentAmount.Amount);
            Assert.Equal(18500m, result.FinalAmount.Amount);
        }

        [Fact]
        public void CalculateBilling_IsDeterministic_SameInputsProduceExactSameAmounts()
        {
            // Arrange
            var sessionId = Guid.NewGuid();
            var session = new Session { SessionId = sessionId, Status = "ENDED" };
            var serverTime = new DateTime(2026, 8, 16, 12, 0, 0, DateTimeKind.Utc);
            var timing = new SessionTimingSnapshot
            {
                SessionId = sessionId,
                ConsumedDuration = TimeSpan.FromMinutes(45),
                CurrentServerTimeUtc = serverTime
            };
            var rateSnapshot = new RateSnapshot
            {
                RateSnapshotId = Guid.NewGuid(),
                SessionId = sessionId,
                PricingPlanId = Guid.NewGuid(),
                RateAmount = 15000m,
                Currency = "SAY"
            };

            // Act
            var result1 = _calculator.CalculateBilling(session, timing, rateSnapshot);
            var result2 = _calculator.CalculateBilling(session, timing, rateSnapshot);

            // Assert
            Assert.Equal(result1.Subtotal.Amount, result2.Subtotal.Amount);
            Assert.Equal(result1.FinalAmount.Amount, result2.FinalAmount.Amount);
            Assert.Equal(11250m, result1.Subtotal.Amount); // 45 mins / 60 * 15000 = 11250
        }

        [Fact]
        public void CalculateBilling_FractionalMinutes_RoundsMoneyToFourDecimalPlaces()
        {
            // Arrange: 33 minutes and 20 seconds @ 12345.6789 SAY/hr
            var sessionId = Guid.NewGuid();
            var session = new Session { SessionId = sessionId, Status = "ACTIVE" };
            var timing = new SessionTimingSnapshot
            {
                SessionId = sessionId,
                ConsumedDuration = TimeSpan.FromMinutes(33) + TimeSpan.FromSeconds(20),
                CurrentServerTimeUtc = DateTime.UtcNow
            };
            var rateSnapshot = new RateSnapshot
            {
                RateSnapshotId = Guid.NewGuid(),
                SessionId = sessionId,
                PricingPlanId = Guid.NewGuid(),
                RateAmount = 12345.6789m,
                Currency = "SAY"
            };

            // Act
            var result = _calculator.CalculateBilling(session, timing, rateSnapshot);

            // Assert
            // (2000 seconds / 3600 seconds) * 12345.6789 = 6858.7105m
            Assert.Equal(6858.7105m, result.Subtotal.Amount);
        }

        [Fact]
        public void CalculateBilling_SessionIdMismatch_ThrowsInvalidDomainException()
        {
            // Arrange
            var session = new Session { SessionId = Guid.NewGuid(), Status = "ACTIVE" };
            var timing = new SessionTimingSnapshot
            {
                SessionId = Guid.NewGuid(), // Different ID
                ConsumedDuration = TimeSpan.FromHours(1)
            };
            var rateSnapshot = new RateSnapshot
            {
                SessionId = session.SessionId,
                PricingPlanId = Guid.NewGuid(),
                RateAmount = 1000m
            };

            // Act & Assert
            var ex = Assert.Throws<InvalidDomainException>(() => _calculator.CalculateBilling(session, timing, rateSnapshot));
            Assert.Equal("SESSION_MISMATCH", ex.ErrorCode);
        }

        [Fact]
        public void BillingResult_NormalizeAndValidate_RequiresSessionIdAndRateSnapshotId()
        {
            // Arrange
            var billing = new BillingResult
            {
                SessionId = Guid.Empty,
                RateSnapshotId = Guid.NewGuid()
            };

            // Act & Assert
            var ex = Assert.Throws<InvalidDomainException>(() => billing.NormalizeAndValidate());
            Assert.Equal("INVALID_SESSION_ID", ex.ErrorCode);
        }

        [Fact]
        public void BillingResult_NormalizeAndValidate_CurrencyMismatch_ThrowsException()
        {
            // Arrange
            var billing = new BillingResult
            {
                SessionId = Guid.NewGuid(),
                RateSnapshotId = Guid.NewGuid(),
                ConsumedDuration = TimeSpan.FromHours(1),
                Currency = "SAY",
                Subtotal = new Money(100, "USD"),
                DiscountAmount = new Money(0, "SAY"),
                AdjustmentAmount = new Money(0, "SAY")
            };

            // Act & Assert
            var ex = Assert.Throws<InvalidDomainException>(() => billing.NormalizeAndValidate());
            Assert.Equal("CURRENCY_MISMATCH", ex.ErrorCode);
        }
    }
}
