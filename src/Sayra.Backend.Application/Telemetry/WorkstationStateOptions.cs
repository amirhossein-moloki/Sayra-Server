using System;

namespace Sayra.Backend.Application.Telemetry
{
    public sealed class WorkstationStateOptions
    {
        public const string SectionName = "Telemetry:WorkstationState";

        /// <summary>
        /// Expected telemetry reporting interval from clients in seconds (default 30s).
        /// </summary>
        public int ExpectedTelemetryIntervalSeconds { get; set; } = 30;

        /// <summary>
        /// Threshold in seconds after which received telemetry is considered stale (default 120s / 2 minutes).
        /// </summary>
        public int TelemetryStaleThresholdSeconds { get; set; } = 120;

        /// <summary>
        /// Timeout threshold in seconds after which a workstation is marked heartbeat stale/timed out (default 90s).
        /// </summary>
        public int HeartbeatTimeoutSeconds { get; set; } = 90;

        /// <summary>
        /// Timeout threshold in seconds after which a disconnected workstation is classified as offline (default 300s / 5 minutes).
        /// </summary>
        public int OfflineTimeoutSeconds { get; set; } = 300;

        /// <summary>
        /// Redis key expiration time to live in hours for real-time state records (default 24h).
        /// </summary>
        public int RedisStateTtlHours { get; set; } = 24;

        public TimeSpan ExpectedTelemetryInterval => TimeSpan.FromSeconds(ExpectedTelemetryIntervalSeconds);
        public TimeSpan TelemetryStaleThreshold => TimeSpan.FromSeconds(TelemetryStaleThresholdSeconds);
        public TimeSpan HeartbeatTimeout => TimeSpan.FromSeconds(HeartbeatTimeoutSeconds);
        public TimeSpan OfflineTimeout => TimeSpan.FromSeconds(OfflineTimeoutSeconds);
        public TimeSpan RedisStateTtl => TimeSpan.FromHours(RedisStateTtlHours);
    }
}
