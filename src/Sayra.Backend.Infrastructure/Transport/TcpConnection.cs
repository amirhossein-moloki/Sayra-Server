using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Transport;

namespace Sayra.Backend.Infrastructure.Transport
{
    public class TcpConnection : ITcpConnection
    {
        private readonly TcpClient _tcpClient;
        private readonly Stream _stream;
        private ConnectionLifecycleState _state;
        private bool _disposed;

        public TcpConnection(string connectionId, TcpClient tcpClient, Stream stream)
        {
            ConnectionId = connectionId ?? throw new ArgumentNullException(nameof(connectionId));
            _tcpClient = tcpClient ?? throw new ArgumentNullException(nameof(tcpClient));
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _state = ConnectionLifecycleState.Connecting;
        }

        public string ConnectionId { get; }

        public ConnectionLifecycleState State => _state;

        public Stream GetStream()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TcpConnection));
            return _stream;
        }

        public void UpdateState(ConnectionLifecycleState newState)
        {
            _state = newState;
        }

        public async Task DisconnectAsync(CancellationToken cancellationToken)
        {
            if (_disposed) return;

            UpdateState(ConnectionLifecycleState.Disconnected);

            try
            {
                _stream.Close();
            }
            catch
            {
                // Ignore closing errors
            }

            try
            {
                _tcpClient.Close();
            }
            catch
            {
                // Ignore closing errors
            }

            await Task.CompletedTask;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                _stream.Dispose();
                _tcpClient.Dispose();
            }
            catch
            {
                // Ignore disposal exceptions
            }
        }
    }
}
