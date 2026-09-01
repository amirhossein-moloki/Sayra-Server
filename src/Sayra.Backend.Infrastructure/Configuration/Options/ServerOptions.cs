namespace Sayra.Backend.Infrastructure.Configuration.Options
{
    public class ServerOptions
    {
        public const string SectionName = "Server";

        public int Port { get; set; } = 5000;
        public string Environment { get; set; } = "Production";
        public string Host { get; set; } = "*";
        public int Backlog { get; set; } = 100;
        public int HandshakeTimeout { get; set; } = 15;
        public int ConnectionTimeout { get; set; } = 300;
        public int MaximumConnections { get; set; } = 1000;
        public int ReceiveBufferSize { get; set; } = 8192;
        public int SendBufferSize { get; set; } = 8192;
        public int MaximumMessageSize { get; set; } = 65536;

        public int HeartbeatInterval { get; set; } = 30;
        public int HeartbeatTimeout { get; set; } = 90;
        public int HeartbeatGracePeriod { get; set; } = 15;
        public int LivenessCheckInterval { get; set; } = 15;
    }
}
