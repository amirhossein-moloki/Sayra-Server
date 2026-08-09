using System;

namespace Sayra.Backend.Domain
{
    public class AuditEvent : BaseEntity
    {
        // Globally unique identifier for offline-event idempotency and duplicate checking
        public Guid EventId { get; set; }
        public string EventType { get; set; } = string.Empty;
        public int EventVersion { get; set; } = 1;
        public Guid? WorkstationId { get; set; }
        public Guid? SessionId { get; set; }
        public string? CorrelationId { get; set; }
        public string? TraceId { get; set; }

        // Payload mapped to JSON/JSONB natively in DB configuration
        public string Payload { get; set; } = "{}";

        public int Priority { get; set; } = 0;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
