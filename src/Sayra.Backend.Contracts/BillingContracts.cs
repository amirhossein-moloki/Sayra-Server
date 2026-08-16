using System;

namespace Sayra.Backend.Contracts
{
    public class CalculateSessionBillingRequestDto
    {
        public decimal? DiscountAmount { get; set; }
        public decimal? AdjustmentAmount { get; set; }
    }

    public class BillingResultResponseDto
    {
        public Guid BillingResultId { get; set; }
        public Guid SessionId { get; set; }
        public TimeSpan ConsumedDuration { get; set; }
        public Guid RateSnapshotId { get; set; }
        public decimal Subtotal { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal AdjustmentAmount { get; set; }
        public decimal FinalAmount { get; set; }
        public string Currency { get; set; } = "SAY";
        public DateTime CalculatedAtUtc { get; set; }
        public string? CorrelationId { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
