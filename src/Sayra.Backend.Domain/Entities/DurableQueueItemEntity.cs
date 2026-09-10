using System;

namespace Sayra.Backend.Domain.Entities
{
    public class DurableQueueItemEntity : BaseEntity
    {
        public string EventId { get; set; } = string.Empty;
        public string EventType { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string WorkstationId { get; set; } = string.Empty;
        public string? SessionId { get; set; }
        public string CorrelationId { get; set; } = string.Empty;
        public long SequenceNumber { get; set; }
        public string ContractVersion { get; set; } = "1.0";
        public string ReliabilityClass { get; set; } = "NORMAL";
        public string Payload { get; set; } = "{}";
        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
        public string Status { get; set; } = OfflineQueueItemStatus.Pending;
        public DateTime? LastAttemptAt { get; set; }
        public DateTime? NextAttemptAt { get; set; }
        public int RetryCount { get; set; } = 0;
        public string? LastError { get; set; }
        public DateTime? ExpiresAt { get; set; }
    }

    public static class OfflineQueueItemStatus
    {
        public const string Pending = "PENDING";
        public const string InFlight = "IN_FLIGHT";
        public const string Acknowledged = "ACKNOWLEDGED";
        public const string Failed = "FAILED";
        public const string Expired = "EXPIRED";
    }
}
