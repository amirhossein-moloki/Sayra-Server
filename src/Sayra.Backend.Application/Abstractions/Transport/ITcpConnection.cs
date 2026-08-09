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
        Stream GetStream();
        void UpdateState(ConnectionLifecycleState newState);
        Task DisconnectAsync(CancellationToken cancellationToken);
    }
}
