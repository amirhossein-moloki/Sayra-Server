using System;

namespace Sayra.Backend.Contracts
{
    public class TelemetryModel
    {
        public double Cpu { get; set; }
        public double Ram { get; set; }
        public double Uptime { get; set; }
        public DateTime Timestamp { get; set; }
        public string? RunningGameName { get; set; }
        public int? RunningGamePid { get; set; }
        public double? RunningGameCpu { get; set; }
        public double? RunningGameRam { get; set; }
        public double? RunningGameDuration { get; set; }
        public int TotalLaunches { get; set; }
        public int TotalCrashes { get; set; }
        public int TotalRestarts { get; set; }
    }
}
