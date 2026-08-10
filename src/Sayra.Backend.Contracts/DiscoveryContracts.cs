using System;

namespace Sayra.Backend.Contracts
{
    public class DiscoveryRequest
    {
        public string Type { get; set; } = "DISCOVER_SAYRA_SERVER";
        public string? ClientId { get; set; }
    }

    public class DiscoveryResponse
    {
        public string Type { get; set; } = "SAYRA_SERVER_RESPONSE";
        public string ServerId { get; set; } = string.Empty;
        public string ServerName { get; set; } = string.Empty;
        public string Ip { get; set; } = string.Empty;
        public int TcpPort { get; set; }
        public int ApiPort { get; set; }
        public string Version { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string Nonce { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
    }
}
