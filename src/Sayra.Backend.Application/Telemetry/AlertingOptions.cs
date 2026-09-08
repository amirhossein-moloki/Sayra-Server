using System;
using System.Collections.Generic;
using Sayra.Backend.Domain.Enums;
using Sayra.Backend.Domain.Telemetry;

namespace Sayra.Backend.Application.Telemetry
{
    public sealed class AlertingOptions
    {
        public const string SectionName = "Telemetry:Alerting";

        public bool IsEnabled { get; set; } = true;
        public int DefaultCooldownSeconds { get; set; } = 300;
        public int DefaultMinSustainedSeconds { get; set; } = 0;
        public int AlertStormThresholdPerMinute { get; set; } = 1000;
        public List<AlertRule> Rules { get; set; } = GetDefaultRules();

        public static List<AlertRule> GetDefaultRules()
        {
            return new List<AlertRule>
            {
                new AlertRule("CPU_SUSTAINED_HIGH", "CPU Sustained High", "CPU utilization exceeds threshold for sustained duration.", AlertSeverity.Warning, 300, true, 0, "HealthEvaluator", 80.0),
                new AlertRule("MEMORY_SUSTAINED_HIGH", "Memory Sustained High", "RAM utilization exceeds threshold for sustained duration.", AlertSeverity.Warning, 300, true, 0, "HealthEvaluator", 85.0),
                new AlertRule("DISK_USAGE_HIGH", "Disk Usage High", "Disk utilization exceeds threshold.", AlertSeverity.Warning, 300, true, 0, "HealthEvaluator", 90.0),
                new AlertRule("TELEMETRY_STALE", "Telemetry Stale", "Workstation telemetry has not been received within threshold.", AlertSeverity.Warning, 300, true, 0, "HealthEvaluator", 120.0),
                new AlertRule("HEARTBEAT_TIMEOUT", "Heartbeat Timeout", "Workstation transport heartbeat has timed out.", AlertSeverity.Error, 300, true, 0, "HealthEvaluator", 90.0),
                new AlertRule("CONNECTION_LOST", "Workstation Connection Lost / Offline", "Workstation has lost connectivity or entered offline state.", AlertSeverity.Critical, 300, true, 0, "HealthEvaluator", 300.0),
                new AlertRule("GAME_CRASH_FREQUENCY_HIGH", "Game Crash Frequency High", "Multiple game crashes detected within window.", AlertSeverity.Error, 300, true, 0, "HealthEvaluator", 2.0),
                new AlertRule("UPDATE_FAILED", "Software Update Failed", "Software or client package update failed to apply.", AlertSeverity.Error, 300, true, 0, "HealthEvaluator", 1.0),
                new AlertRule("CONFIG_SYNC_FAILED", "Configuration Sync Failed", "Configuration synchronization failed repeatedly.", AlertSeverity.Warning, 300, true, 0, "HealthEvaluator", 1.0),
                new AlertRule("SECURITY_EVENT_FLAGGED", "Security Event Flagged", "Security audit flag or threat detected.", AlertSeverity.Critical, 0, false, 0, "SecurityEventService", 1.0)
            };
        }
    }
}
