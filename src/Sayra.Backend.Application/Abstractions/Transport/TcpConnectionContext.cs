using System;

namespace Sayra.Backend.Application.Abstractions.Transport
{
    public class TcpConnectionContext
    {
        public string ConnectionId { get; set; } = string.Empty;
        public string? PcId { get; set; }
        public byte[]? SessionKey { get; set; }
        public string? IPAddress { get; set; }
        public string? Hostname { get; set; }
        public DateTime ConnectedAt { get; set; }
        public DateTime LastActivity { get; set; }
        public ConnectionLifecycleState ConnectionState { get; set; }

        public static TcpConnectionContext FromConnection(ITcpConnection connection)
        {
            return new TcpConnectionContext
            {
                ConnectionId = connection.ConnectionId,
                PcId = connection.PcId,
                SessionKey = connection.SessionKey,
                IPAddress = connection.RemoteIpAddress,
                Hostname = connection.Hostname,
                ConnectedAt = connection.ConnectedAt,
                LastActivity = connection.LastActivity,
                ConnectionState = connection.State
            };
        }
    }
}
