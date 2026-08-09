using System;

namespace Sayra.Backend.Domain
{
    public class WorkstationSession : BaseEntity
    {
        public Guid WorkstationId { get; set; }
        public Guid GamerId { get; set; }
        public DateTime StartTime { get; set; } = DateTime.UtcNow;
        public DateTime? EndTime { get; set; }

        // Use decimal for precise currency representation to avoid floating-point errors
        public decimal RatePerHour { get; set; }
        public decimal CurrentCost { get; set; }
        public decimal RemainingCredits { get; set; }
        public decimal BillingAmount { get; set; }
        public string Currency { get; set; } = "SAY";

        public string SessionState { get; set; } = "Active"; // Active, Completed, Suspended

        // Optimistic concurrency token
        public uint RowVersion { get; set; }
    }
}
