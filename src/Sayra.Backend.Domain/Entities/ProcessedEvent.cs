using System;

namespace Sayra.Backend.Domain.Entities
{
    public class ProcessedEvent : BaseEntity
    {
        // Globally unique EventId for offline-event idempotency and duplicate checking
        public Guid EventId { get; set; }
        public string BatchId { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public Guid? WorkstationId { get; set; }
        public string EventType { get; set; } = string.Empty;
        public long SequenceNumber { get; set; }
        public string ReliabilityClass { get; set; } = "NORMAL";
        public string ProcessingStatus { get; set; } = "ACCEPTED"; // ACCEPTED, DUPLICATE, REJECTED, CONFLICT
        public string PayloadHash { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
        public DateTime FirstReceivedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastReceivedAt { get; set; } = DateTime.UtcNow;
        public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
    }
}
