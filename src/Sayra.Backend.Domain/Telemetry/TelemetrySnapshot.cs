using System;
using Sayra.Backend.Domain.ValueObjects;

namespace Sayra.Backend.Domain.Telemetry
{
    public sealed class TelemetrySnapshot
    {
        public WorkstationIdentity Identity { get; }
        public double Cpu { get; }
        public double Ram { get; }
        public double Uptime { get; }
        public string? RunningGameName { get; }
        public int? RunningGamePid { get; }
        public double? RunningGameCpu { get; }
        public double? RunningGameRam { get; }
        public double? RunningGameDuration { get; }
        public int TotalLaunches { get; }
        public int TotalCrashes { get; }
        public int TotalRestarts { get; }
        public DateTime ClientTimestamp { get; }
        public DateTime ServerReceivedAt { get; }
        public DateTime ProcessedAt { get; }

        public TelemetrySnapshot(
            WorkstationIdentity identity,
            double cpu,
            double ram,
            double uptime,
            string? runningGameName,
            int? runningGamePid,
            double? runningGameCpu,
            double? runningGameRam,
            double? runningGameDuration,
            int totalLaunches,
            int totalCrashes,
            int totalRestarts,
            DateTime clientTimestamp,
            DateTime serverReceivedAt,
            DateTime processedAt)
        {
            Identity = identity ?? throw new ArgumentNullException(nameof(identity));
            Cpu = cpu;
            Ram = ram;
            Uptime = uptime;
            RunningGameName = runningGameName;
            RunningGamePid = runningGamePid;
            RunningGameCpu = runningGameCpu;
            RunningGameRam = runningGameRam;
            RunningGameDuration = runningGameDuration;
            TotalLaunches = totalLaunches;
            TotalCrashes = totalCrashes;
            TotalRestarts = totalRestarts;
            ClientTimestamp = clientTimestamp;
            ServerReceivedAt = serverReceivedAt;
            ProcessedAt = processedAt;
        }
    }
}
