using System;

namespace Sayra.Backend.Application.Telemetry
{
    public sealed class WorkstationHealthPolicyOptions
    {
        public const string SectionName = "Telemetry:HealthPolicy";

        // CPU Thresholds & Recovery
        public double CpuWarningThresholdPercent { get; set; } = 80.0;
        public double CpuCriticalThresholdPercent { get; set; } = 95.0;
        public double CpuRecoveryThresholdPercent { get; set; } = 75.0;
        public int CpuSustainedDurationSeconds { get; set; } = 300; // 5 minutes

        // RAM Thresholds & Recovery
        public double RamWarningThresholdPercent { get; set; } = 85.0;
        public double RamCriticalThresholdPercent { get; set; } = 95.0;
        public double RamRecoveryThresholdPercent { get; set; } = 80.0;
        public int RamSustainedDurationSeconds { get; set; } = 300; // 5 minutes

        // Operational Event / Crash Thresholds
        public int GameCrashWindowMinutes { get; set; } = 15;
        public int GameCrashWarningThresholdCount { get; set; } = 2;
        public int GameCrashCriticalThresholdCount { get; set; } = 4;

        // Freshness & Connectivity Rules
        public int TelemetryStaleThresholdSeconds { get; set; } = 120;
        public int HeartbeatTimeoutSeconds { get; set; } = 90;
        public int OfflineTimeoutSeconds { get; set; } = 300;

        // Evaluation Worker Cadence & Batching
        public int EvaluationIntervalSeconds { get; set; } = 30;
        public int EvaluationBatchSize { get; set; } = 100;

        // Helper TimeSpans
        public TimeSpan CpuSustainedDuration => TimeSpan.FromSeconds(CpuSustainedDurationSeconds);
        public TimeSpan RamSustainedDuration => TimeSpan.FromSeconds(RamSustainedDurationSeconds);
        public TimeSpan TelemetryStaleThreshold => TimeSpan.FromSeconds(TelemetryStaleThresholdSeconds);
        public TimeSpan HeartbeatTimeout => TimeSpan.FromSeconds(HeartbeatTimeoutSeconds);
        public TimeSpan OfflineTimeout => TimeSpan.FromSeconds(OfflineTimeoutSeconds);
        public TimeSpan EvaluationInterval => TimeSpan.FromSeconds(EvaluationIntervalSeconds);
    }
}
