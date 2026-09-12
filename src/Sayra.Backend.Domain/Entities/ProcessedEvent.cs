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
        public string OrderingStatus { get; set; } = "UNORDERED"; // IN_ORDER, OUT_OF_ORDER, GAP_DETECTED, DUPLICATE, STALE, SEQUENCE_CONFLICT, UNORDERED
        public string ProcessingStatus { get; set; } = "ACCEPTED"; // ACCEPTED, READY_FOR_RECONCILIATION, WAITING_FOR_SEQUENCE, DUPLICATE, SEQUENCE_CONFLICT, CONFLICT, EXPIRED, INVALID, REJECTED
        public string PayloadHash { get; set; } = string.Empty;
        public string? ReasonCode { get; set; }
        public string? ErrorMessage { get; set; }
        public string? ConflictMetadata { get; set; }
        public DateTime? OccurredAt { get; set; }
        public DateTime FirstReceivedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastReceivedAt { get; set; } = DateTime.UtcNow;
        public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ReconciledAt { get; set; }
    }
}
