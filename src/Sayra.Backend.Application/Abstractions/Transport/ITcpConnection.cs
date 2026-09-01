using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Sayra.Backend.Application.Abstractions.Transport
{
    public interface ITcpConnection : IDisposable
    {
        string ConnectionId { get; }
        ConnectionLifecycleState State { get; }
        byte[]? SessionKey { get; set; }
        string? PcId { get; set; }
        string? Hostname { get; set; }
        string? SiteId { get; set; }
        string? ClientVersion { get; set; }
        string? RemoteIpAddress { get; }
        DateTime ConnectedAt { get; }
        DateTime LastActivity { get; set; }
        Stream GetStream();
        void UpdateState(ConnectionLifecycleState newState);
        Task SendAsync(byte[] data, CancellationToken cancellationToken = default);
        Task SendFrameAsync(string frame, CancellationToken cancellationToken = default);
        Task DisconnectAsync(CancellationToken cancellationToken);
    }
}
