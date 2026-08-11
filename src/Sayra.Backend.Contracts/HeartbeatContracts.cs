using System;

namespace Sayra.Backend.Contracts
{
    public class PingMessage
    {
        public string Type { get; set; } = "PING";
        public string Command { get; set; } = "PING";
        public DateTime Timestamp { get; set; }
    }

    public class HeartbeatMessage
    {
        public string Type { get; set; } = "HEARTBEAT";
        public string PcId { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }

    public class PongMessage
    {
        public string Type { get; set; } = "PONG";
        public DateTime Timestamp { get; set; }
    }
}
