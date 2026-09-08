using System;
using System.Collections.Generic;
using Sayra.Backend.Domain.Enums;

namespace Sayra.Backend.Application.Telemetry
{
    public class FleetWorkstationSummaryDto
    {
        public int TotalTrackedWorkstations { get; set; }
        public int OnlineCount { get; set; }
        public int OfflineCount { get; set; }
        public int HealthyCount { get; set; }
        public int WarningCount { get; set; }
        public int DegradedCount { get; set; }
        public int CriticalCount { get; set; }
        public int UnknownCount { get; set; }
        public DateTime EvaluatedAtUtc { get; set; }
    }

    public class FleetWorkstationItemDto
    {
        public Guid WorkstationId { get; set; }
        public string PcId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public Guid? SiteId { get; set; }
        public Guid? OrganizationId { get; set; }
        public string Status { get; set; } = "OFFLINE";
        public string ConnectionState { get; set; } = "Disconnected";
        public bool IsConnected { get; set; }
        public DateTime? LastSeenAt { get; set; }
        public DateTime? LastTelemetryReceivedAt { get; set; }
        public DateTime? LastHeartbeatReceivedAt { get; set; }
        public WorkstationHealthState HealthState { get; set; } = WorkstationHealthState.Unknown;
        public double HealthScore { get; set; } = 100.0;
        public List<WorkstationHealthReasonDto> HealthReasons { get; set; } = new List<WorkstationHealthReasonDto>();
        public int ActiveIncidentCount { get; set; }
        public string ClientVersion { get; set; } = string.Empty;
        public string OsVersion { get; set; } = string.Empty;
    }

    public class FleetWorkstationsResponseDto
    {
        public FleetWorkstationSummaryDto Summary { get; set; } = new FleetWorkstationSummaryDto();
        public IReadOnlyList<FleetWorkstationItemDto> Items { get; set; } = new List<FleetWorkstationItemDto>();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }

    public class WorkstationDetailDto
    {
        public Guid WorkstationId { get; set; }
        public string PcId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public Guid? SiteId { get; set; }
        public Guid? OrganizationId { get; set; }
        public string Hostname { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string MacAddress { get; set; } = string.Empty;
        public string Status { get; set; } = "OFFLINE";
        public string ClientVersion { get; set; } = string.Empty;
        public string OsVersion { get; set; } = string.Empty;
        public bool IsDisabled { get; set; }
        public bool IsProvisioned { get; set; }
        public DateTime? ProvisionedAt { get; set; }
        public string? ConnectionId { get; set; }
        public bool IsConnected { get; set; }
        public string ConnectionState { get; set; } = "Disconnected";
        public DateTime? LastSeenAt { get; set; }
        public DateTime? LastTelemetryReceivedAt { get; set; }
        public DateTime? LastHeartbeatReceivedAt { get; set; }
        public LatestWorkstationMetricsDto? LatestMetrics { get; set; }
        public WorkstationHealthDetailDto Health { get; set; } = new WorkstationHealthDetailDto();
        public IReadOnlyList<IncidentSummaryDto> ActiveIncidents { get; set; } = new List<IncidentSummaryDto>();
    }

    public class LatestWorkstationMetricsDto
    {
        public double Cpu { get; set; }
        public double Ram { get; set; }
        public long Uptime { get; set; }
        public long TotalLaunches { get; set; }
        public long TotalCrashes { get; set; }
        public long TotalRestarts { get; set; }
        public string? RunningGameId { get; set; }
        public double? GameCpu { get; set; }
        public double? GameRam { get; set; }
        public long? GameDuration { get; set; }
        public DateTime TimestampUtc { get; set; }
    }

    public class WorkstationHealthDetailDto
    {
        public WorkstationHealthState HealthState { get; set; } = WorkstationHealthState.Unknown;
        public double HealthScore { get; set; } = 100.0;
        public List<WorkstationHealthReasonDto> Reasons { get; set; } = new List<WorkstationHealthReasonDto>();
        public DateTime EvaluatedAtUtc { get; set; }
        public DateTime? LastTelemetryReceivedAtUtc { get; set; }
        public DateTime? LastHeartbeatReceivedAtUtc { get; set; }
        public int ActiveIncidentCount { get; set; }
    }

    public class WorkstationHealthReasonDto
    {
        public string ReasonCode { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public WorkstationHealthState Severity { get; set; } = WorkstationHealthState.Warning;
        public DateTime FirstDetectedAtUtc { get; set; }
        public double? Value { get; set; }
        public double? Threshold { get; set; }
    }

    public class HistoricalMetricPointDto
    {
        public DateTime TimestampUtc { get; set; }
        public double? Cpu { get; set; }
        public double? Ram { get; set; }
        public long? Uptime { get; set; }
        public long? Launches { get; set; }
        public long? Crashes { get; set; }
        public long? Restarts { get; set; }
        public double? CpuMin { get; set; }
        public double? CpuMax { get; set; }
        public double? CpuP95 { get; set; }
        public double? RamMin { get; set; }
        public double? RamMax { get; set; }
        public double? RamP95 { get; set; }
        public int? SampleCount { get; set; }
    }

    public class HistoricalMetricsResponseDto
    {
        public Guid WorkstationId { get; set; }
        public string PcId { get; set; } = string.Empty;
        public string Resolution { get; set; } = "raw";
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public int TotalPoints { get; set; }
        public string? NextCursor { get; set; }
        public IReadOnlyList<HistoricalMetricPointDto> Points { get; set; } = new List<HistoricalMetricPointDto>();
    }

    public class MonitoringEventDto
    {
        public Guid Id { get; set; }
        public Guid EventId { get; set; }
        public string EventType { get; set; } = string.Empty;
        public Guid? WorkstationId { get; set; }
        public Guid? SessionId { get; set; }
        public string? CorrelationId { get; set; }
        public int Priority { get; set; }
        public DateTime TimestampUtc { get; set; }
        public string Payload { get; set; } = "{}";
    }

    public class IncidentSummaryDto
    {
        public Guid Id { get; set; }
        public string Fingerprint { get; set; } = string.Empty;
        public string RuleName { get; set; } = string.Empty;
        public AlertSeverity Severity { get; set; }
        public IncidentLifecycleState LifecycleState { get; set; }
        public Guid? WorkstationId { get; set; }
        public string? PcId { get; set; }
        public Guid? SiteId { get; set; }
        public Guid? OrganizationId { get; set; }
        public string Summary { get; set; } = string.Empty;
        public DateTime FirstTriggeredAtUtc { get; set; }
        public DateTime LastObservedAtUtc { get; set; }
        public DateTime? ResolvedAtUtc { get; set; }
    }

    public class IncidentDetailDto
    {
        public Guid Id { get; set; }
        public string Fingerprint { get; set; } = string.Empty;
        public string RuleName { get; set; } = string.Empty;
        public AlertSeverity Severity { get; set; }
        public IncidentLifecycleState LifecycleState { get; set; }
        public Guid? WorkstationId { get; set; }
        public string? PcId { get; set; }
        public Guid? SiteId { get; set; }
        public Guid? OrganizationId { get; set; }
        public string Summary { get; set; } = string.Empty;
        public DateTime FirstTriggeredAtUtc { get; set; }
        public DateTime LastObservedAtUtc { get; set; }
        public DateTime? FiringAtUtc { get; set; }
        public DateTime? ResolvedAtUtc { get; set; }
        public List<string> Reasons { get; set; } = new List<string>();
        public string Evidence { get; set; } = "{}";
        public string PolicyVersion { get; set; } = string.Empty;
        public string? CorrelationId { get; set; }
        public string? SuppressionKey { get; set; }
        public uint RowVersion { get; set; }
    }

    public class MonitoringPagedResult<T>
    {
        public IReadOnlyList<T> Items { get; set; } = new List<T>();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public string? NextCursor { get; set; }
    }
}
