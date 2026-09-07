namespace Sayra.Backend.Infrastructure.Configuration.Options
{
    public class TelemetryAggregationOptions
    {
        public const string SectionName = "TelemetryAggregation";

        public bool Enabled { get; set; } = true;
        public int IntervalSeconds { get; set; } = 60;
        public int BatchSize { get; set; } = 1000;
        public int LatenesWindowMinutes { get; set; } = 15;
    }
}
