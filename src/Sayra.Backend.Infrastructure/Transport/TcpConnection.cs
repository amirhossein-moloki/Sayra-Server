using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Transport;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Infrastructure.Transport
{
    public class TcpConnection : ITcpConnection
    {
        private readonly TcpClient _tcpClient;
        private readonly Stream _stream;
        private readonly object _stateLock = new();
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private ConnectionLifecycleState _state;
        private bool _disposed;

        public TcpConnection(string connectionId, TcpClient tcpClient, Stream stream)
        {
            ConnectionId = connectionId ?? throw new ArgumentNullException(nameof(connectionId));
            _tcpClient = tcpClient ?? throw new ArgumentNullException(nameof(tcpClient));
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _state = ConnectionLifecycleState.Connecting;
            ConnectedAt = DateTime.UtcNow;
            LastActivity = DateTime.UtcNow;
        }

        public string ConnectionId { get; }

        public ConnectionLifecycleState State
        {
            get
            {
                lock (_stateLock)
                {
                    return _state;
                }
            }
        }

        public byte[]? SessionKey { get; set; }
        public string? PcId { get; set; }
        public string? Hostname { get; set; }
        public string? SiteId { get; set; }
        public string? ClientVersion { get; set; }
        public DateTime ConnectedAt { get; }
        public DateTime LastActivity { get; set; }

        public string? RemoteIpAddress
        {
            get
            {
                try
                {
                    if (_tcpClient.Client?.RemoteEndPoint is System.Net.IPEndPoint ipEndPoint)
                    {
                        return ipEndPoint.Address.ToString();
                    }
                }
                catch
                {
                    // Ignore and fall back
                }
                return null;
            }
        }

        public Stream GetStream()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TcpConnection));
            return _stream;
        }

        public void UpdateState(ConnectionLifecycleState newState)
        {
            lock (_stateLock)
            {
                ConnectionLifecycleValidator.ValidateTransition(_state, newState);
                _state = newState;
            }
        }

        public async Task SendAsync(byte[] data, CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TcpConnection));

            if (data == null || data.Length == 0)
                return;

            await _writeLock.WaitAsync(cancellationToken);
            try
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(TcpConnection));

                await _stream.WriteAsync(data, 0, data.Length, cancellationToken);
                await _stream.FlushAsync(cancellationToken);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public async Task SendFrameAsync(string frame, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(frame))
                return;

            string line = frame.EndsWith("\n") ? frame : frame + "\n";
            byte[] bytes = Encoding.UTF8.GetBytes(line);
            await SendAsync(bytes, cancellationToken);
        }

        public async Task DisconnectAsync(CancellationToken cancellationToken)
        {
            if (_disposed) return;

            try
            {
                UpdateState(ConnectionLifecycleState.Disconnected);
            }
            catch
            {
                // Already disconnected or state transition ignored on disconnect
            }

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
                _writeLock.Dispose();
            }
            catch
            {
                // Ignore disposal exceptions
            }
        }
    }
}
