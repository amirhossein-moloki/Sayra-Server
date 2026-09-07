using System;
using Sayra.Backend.Domain.Telemetry;
using Sayra.Backend.Domain.ValueObjects;

namespace Sayra.Backend.Domain.Entities
{
    public class TelemetryHistoryRecord : BaseEntity
    {
        public Guid WorkstationId { get; set; }
        public Guid? OrganizationId { get; set; }
        public Guid? SiteId { get; set; }
        public string PcId { get; set; } = string.Empty;
        public string? SessionId { get; set; }
        public string? ConnectionId { get; set; }
        public double Cpu { get; set; }
        public double Ram { get; set; }
        public double Uptime { get; set; }
        public string? RunningGameName { get; set; }
        public int? RunningGamePid { get; set; }
        public double? RunningGameCpu { get; set; }
        public double? RunningGameRam { get; set; }
        public double? RunningGameDuration { get; set; }
        public int TotalLaunches { get; set; }
        public int TotalCrashes { get; set; }
        public int TotalRestarts { get; set; }
        public DateTime ClientTimestamp { get; set; }
        public DateTime ServerReceivedAt { get; set; }
        public DateTime ProcessedAt { get; set; }

        public TelemetryHistoryRecord()
        {
        }

        public static TelemetryHistoryRecord FromSnapshot(
            TelemetrySnapshot snapshot,
            string? connectionId = null,
            string? sessionId = null)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            return new TelemetryHistoryRecord
            {
                WorkstationId = snapshot.Identity.WorkstationId ?? Guid.Empty,
                OrganizationId = snapshot.Identity.OrganizationId,
                SiteId = snapshot.Identity.SiteId,
                PcId = snapshot.Identity.PcId,
                SessionId = sessionId,
                ConnectionId = connectionId,
                Cpu = snapshot.Cpu,
                Ram = snapshot.Ram,
                Uptime = snapshot.Uptime,
                RunningGameName = snapshot.RunningGameName,
                RunningGamePid = snapshot.RunningGamePid,
                RunningGameCpu = snapshot.RunningGameCpu,
                RunningGameRam = snapshot.RunningGameRam,
                RunningGameDuration = snapshot.RunningGameDuration,
                TotalLaunches = snapshot.TotalLaunches,
                TotalCrashes = snapshot.TotalCrashes,
                TotalRestarts = snapshot.TotalRestarts,
                ClientTimestamp = snapshot.ClientTimestamp,
                ServerReceivedAt = snapshot.ServerReceivedAt,
                ProcessedAt = snapshot.ProcessedAt
            };
        }
    }
}
