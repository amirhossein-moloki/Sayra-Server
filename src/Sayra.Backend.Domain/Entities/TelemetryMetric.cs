using System;

namespace Sayra.Backend.Domain
{
    public class TelemetryMetric : BaseEntity
    {
        public Guid WorkstationId { get; set; }
        public string MetricName { get; set; } = string.Empty;
        public double MetricValue { get; set; }
        public string DimensionJson { get; set; } = "{}"; // JSON string configured as JSONB in DB configuration
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
