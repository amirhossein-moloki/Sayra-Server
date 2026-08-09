namespace Sayra.Backend.Infrastructure.Configuration.Options
{
    public class TelemetryOptions
    {
        public const string SectionName = "Telemetry";

        public int BatchSize { get; set; } = 100;
        public int FlushIntervalSeconds { get; set; } = 10;
        public int RetentionDays { get; set; } = 90;
    }
}
