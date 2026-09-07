using System;
using Sayra.Backend.Domain.ValueObjects;

namespace Sayra.Backend.Domain.Telemetry
{
    public enum WorkstationOperationalState
    {
        Unknown = 0,
        ConnectedFresh = 1,
        ConnectedStale = 2,
        Disconnected = 3,
        Offline = 4
    }

    public sealed class WorkstationRealTimeState
    {
        // Identity
        public string PcId { get; set; } = string.Empty;
        public Guid? WorkstationId { get; set; }
        public Guid? SiteId { get; set; }
        public Guid? OrganizationId { get; set; }

        // Transport Connection State
        public string? ConnectionId { get; set; }
        public bool IsConnected { get; set; }
        public string? ConnectionState { get; set; }

        // Timestamps (UTC)
        public DateTime? LastSeenAt { get; set; }
        public DateTime? LastTelemetryReceivedAt { get; set; }
        public DateTime? LastTelemetryClientTimestamp { get; set; }
        public DateTime? LastHeartbeatReceivedAt { get; set; }
        public DateTime? LastHeartbeatClientTimestamp { get; set; }
        public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;

        // System Usage Metrics
        public double? Cpu { get; set; }
        public double? Ram { get; set; }
        public double? Uptime { get; set; }

        // Running Application / Game Metrics
        public string? RunningGameName { get; set; }
        public int? RunningGamePid { get; set; }
        public double? RunningGameCpu { get; set; }
        public double? RunningGameRam { get; set; }
        public double? RunningGameDuration { get; set; }

        // Operational Counters
        public int? TotalLaunches { get; set; }
        public int? TotalCrashes { get; set; }
        public int? TotalRestarts { get; set; }

        // Session & Software Update Associations
        public string? CurrentSessionId { get; set; }
        public string? UpdateState { get; set; }

        // Last Operational Event Summary
        public string? LastEventId { get; set; }
        public string? LastEventType { get; set; }
        public DateTime? LastEventOccurredAt { get; set; }

        public WorkstationRealTimeState()
        {
        }

        public WorkstationRealTimeState(WorkstationIdentity identity)
        {
            if (identity == null) throw new ArgumentNullException(nameof(identity));
            PcId = identity.PcId;
            WorkstationId = identity.WorkstationId;
            SiteId = identity.SiteId;
            OrganizationId = identity.OrganizationId;
        }

        public WorkstationIdentity GetIdentity()
        {
            return new WorkstationIdentity(PcId, WorkstationId, SiteId, OrganizationId);
        }

        public bool IsTelemetryFresh(DateTime nowUtc, TimeSpan staleThreshold)
        {
            if (!LastTelemetryReceivedAt.HasValue) return false;
            return (nowUtc - LastTelemetryReceivedAt.Value) <= staleThreshold;
        }

        public bool IsHeartbeatFresh(DateTime nowUtc, TimeSpan heartbeatTimeout)
        {
            if (!LastHeartbeatReceivedAt.HasValue) return false;
            return (nowUtc - LastHeartbeatReceivedAt.Value) <= heartbeatTimeout;
        }

        public bool IsStale(DateTime nowUtc, TimeSpan staleThreshold)
        {
            return !IsTelemetryFresh(nowUtc, staleThreshold);
        }

        public WorkstationOperationalState EvaluateOperationalState(
            DateTime nowUtc,
            TimeSpan staleThreshold,
            TimeSpan offlineTimeout)
        {
            if (IsConnected)
            {
                return IsTelemetryFresh(nowUtc, staleThreshold)
                    ? WorkstationOperationalState.ConnectedFresh
                    : WorkstationOperationalState.ConnectedStale;
            }

            if (LastSeenAt.HasValue)
            {
                var timeSinceLastSeen = nowUtc - LastSeenAt.Value;
                if (timeSinceLastSeen <= offlineTimeout)
                {
                    return WorkstationOperationalState.Disconnected;
                }

                return WorkstationOperationalState.Offline;
            }

            return WorkstationOperationalState.Unknown;
        }
    }
}
