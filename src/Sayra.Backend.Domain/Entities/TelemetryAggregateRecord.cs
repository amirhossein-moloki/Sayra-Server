using System;

namespace Sayra.Backend.Domain.Entities
{
    public class TelemetryAggregateRecord : BaseEntity
    {
        public Guid WorkstationId { get; set; }
        public Guid? OrganizationId { get; set; }
        public Guid? SiteId { get; set; }
        public string PcId { get; set; } = string.Empty;

        public string Granularity { get; set; } = "1m"; // "1m", "5m", "1h", "1d"
        public DateTime WindowStart { get; set; }
        public DateTime WindowEnd { get; set; }
        public int SampleCount { get; set; }
        public bool IsPartial { get; set; }

        // System CPU Gauges
        public double CpuMin { get; set; }
        public double CpuMax { get; set; }
        public double CpuAvg { get; set; }
        public double CpuP50 { get; set; }
        public double CpuP95 { get; set; }
        public double CpuP99 { get; set; }

        // System RAM Gauges (MB)
        public double RamMin { get; set; }
        public double RamMax { get; set; }
        public double RamAvg { get; set; }
        public double RamP50 { get; set; }
        public double RamP95 { get; set; }
        public double RamP99 { get; set; }

        // Monotonic Counter Deltas
        public double UptimeDelta { get; set; }
        public int LaunchesDelta { get; set; }
        public int CrashesDelta { get; set; }
        public int RestartsDelta { get; set; }

        // Active Game Metrics
        public string? PrimaryGameName { get; set; }
        public int GameSampleCount { get; set; }
        public double? GameCpuAvg { get; set; }
        public double? GameCpuMax { get; set; }
        public double? GameRamAvg { get; set; }
        public double? GameRamMax { get; set; }
        public double? GameDurationMax { get; set; }

        public TelemetryAggregateRecord()
        {
        }
    }
}
