namespace Sayra.Backend.Domain.Telemetry
{
    public static class WorkstationHealthReasonCode
    {
        public const string CpuSustainedHigh = "CPU_SUSTAINED_HIGH";
        public const string MemorySustainedHigh = "MEMORY_SUSTAINED_HIGH";
        public const string DiskUsageHigh = "DISK_USAGE_HIGH";
        public const string TelemetryStale = "TELEMETRY_STALE";
        public const string HeartbeatTimeout = "HEARTBEAT_TIMEOUT";
        public const string ConnectionLost = "CONNECTION_LOST";
        public const string GameCrashFrequencyHigh = "GAME_CRASH_FREQUENCY_HIGH";
        public const string UpdateFailed = "UPDATE_FAILED";
        public const string ConfigSyncFailed = "CONFIG_SYNC_FAILED";
        public const string SecurityEventFlagged = "SECURITY_EVENT_FLAGGED";
    }
}
