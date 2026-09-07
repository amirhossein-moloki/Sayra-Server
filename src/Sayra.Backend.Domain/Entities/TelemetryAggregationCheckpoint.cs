using System;

namespace Sayra.Backend.Domain.Entities
{
    public class TelemetryAggregationCheckpoint : BaseEntity
    {
        public string Granularity { get; set; } = "1m"; // "1m", "5m", "1h"
        public DateTime LastProcessedWindowEnd { get; set; }
        public DateTime LastProcessedServerTimestamp { get; set; }
        public int RecordsProcessed { get; set; }

        public TelemetryAggregationCheckpoint()
        {
        }
    }
}
