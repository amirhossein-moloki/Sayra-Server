using System;

namespace Sayra.Backend.Domain.Entities
{
    public class DeadLetterEvent : BaseEntity
    {
        public Guid EventId { get; set; }
        public string BatchId { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public Guid? WorkstationId { get; set; }
        public string? SiteId { get; set; }
        public Guid? OrganizationId { get; set; }
        public string EventType { get; set; } = string.Empty;
        public long SequenceNumber { get; set; }
        public string ReliabilityClass { get; set; } = "NORMAL";
        public string Payload { get; set; } = "{}";
        public string FailureCode { get; set; } = string.Empty;
        public string FailureReason { get; set; } = string.Empty;
        public int RetryCount { get; set; }
        public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;
        public DateTime LastAttemptAt { get; set; } = DateTime.UtcNow;
        public DateTime DeadLetteredAt { get; set; } = DateTime.UtcNow;
        public string CorrelationId { get; set; } = string.Empty;
        public string ProcessingStatus { get; set; } = DeadLetterStatus.DeadLetter;
        public DateTime? RecoveredAt { get; set; }
        public string? RecoveredBy { get; set; }
    }

    public static class DeadLetterStatus
    {
        public const string DeadLetter = "DEAD_LETTER";
        public const string Expired = "EXPIRED";
        public const string Rejected = "REJECTED";
        public const string Recovered = "RECOVERED";
    }
}
