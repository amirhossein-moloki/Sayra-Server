using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sayra.Backend.Application.Abstractions.Transport;
using Sayra.Backend.Infrastructure.Configuration.Options;

namespace Sayra.Backend.Infrastructure.Transport
{
    public class TcpServer : IHostedService, ITcpServer, IDisposable
    {
        private readonly ITcpConnectionRegistry _connectionRegistry;
        private readonly ServerOptions _serverOptions;
        private readonly TlsOptions _tlsOptions;
        private readonly ILogger<TcpServer> _logger;
        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        private Task? _listenerTask;
        private X509Certificate2? _serverCertificate;
        private readonly object _lock = new();

        public TcpServer(
            ITcpConnectionRegistry connectionRegistry,
            IOptions<ServerOptions> serverOptions,
            IOptions<TlsOptions> tlsOptions,
            ILogger<TcpServer> logger)
        {
            _connectionRegistry = connectionRegistry ?? throw new ArgumentNullException(nameof(connectionRegistry));
            _serverOptions = serverOptions?.Value ?? throw new ArgumentNullException(nameof(serverOptions));
            _tlsOptions = tlsOptions?.Value ?? throw new ArgumentNullException(nameof(tlsOptions));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting TCP Server transport foundation...");

            lock (_lock)
            {
                if (_listener != null)
                {
                    _logger.LogWarning("TCP Server is already running.");
                    return;
                }

                _cts = new CancellationTokenSource();

                // Prepare TLS Certificate if configured
                LoadCertificate();

                // Start Listening
                var ipAddress = IPAddress.Any;
                if (!string.IsNullOrEmpty(_serverOptions.Host) && _serverOptions.Host != "*")
                {
                    if (IPAddress.TryParse(_serverOptions.Host, out var parsedIp))
                    {
                        ipAddress = parsedIp;
                    }
                }

                _listener = new TcpListener(ipAddress, _serverOptions.Port);
                try
                {
                    _listener.Start();
                }
                catch (Exception ex)
                {
                    _logger.LogCritical(ex, "Failed to start TcpListener on port {Port}.", _serverOptions.Port);
                    throw;
                }

                _logger.LogInformation("TCP Server successfully listening on {Host}:{Port}.", ipAddress, _serverOptions.Port);

                _listenerTask = AcceptConnectionsAsync(_cts.Token);
            }

            await Task.CompletedTask;
        }

        private void LoadCertificate()
        {
            if (!string.IsNullOrEmpty(_tlsOptions.CertificatePath))
            {
                try
                {
                    if (File.Exists(_tlsOptions.CertificatePath))
                    {
                        _serverCertificate = new X509Certificate2(
                            _tlsOptions.CertificatePath,
                            _tlsOptions.CertificatePassword,
                            X509KeyStorageFlags.DefaultKeySet);

                        _logger.LogInformation("Successfully loaded SSL/TLS certificate from {Path}.", _tlsOptions.CertificatePath);
                    }
                    else
                    {
                        _logger.LogWarning("Certificate file not found at {Path}. Falling back to unencrypted TCP.", _tlsOptions.CertificatePath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load SSL/TLS certificate from {Path}. Falling back to unencrypted TCP.", _tlsOptions.CertificatePath);
                }
            }
        }

        private async Task AcceptConnectionsAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var tcpClient = await _listener!.AcceptTcpClientAsync(cancellationToken);
                    _logger.LogInformation("New TCP client connection request accepted from {RemoteEndPoint}.", tcpClient.Client.RemoteEndPoint);

                    // Handle client in background task to process multiple concurrent clients safely
                    _ = Task.Run(() => HandleClientAsync(tcpClient, cancellationToken), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (cancellationToken.IsCancellationRequested || ex is SocketException || ex is ObjectDisposedException)
                    {
                        break;
                    }
                    _logger.LogError(ex, "Error occurred while accepting a TCP connection.");
                }
            }
        }

        private async Task HandleClientAsync(TcpClient tcpClient, CancellationToken cancellationToken)
        {
            var connectionId = Guid.NewGuid().ToString();
            ITcpConnection? connection = null;

            try
            {
                Stream networkStream = tcpClient.GetStream();

                if (_serverCertificate != null)
                {
                    _logger.LogInformation("Negotiating TLS 1.3 handshake on connection {ConnectionId}...", connectionId);

                    var sslStream = new SslStream(networkStream, false);
                    var sslOptions = new SslServerAuthenticationOptions
                    {
                        ServerCertificate = _serverCertificate,
                        ClientCertificateRequired = _tlsOptions.RequireClientCertificate,
                        EnabledSslProtocols = SslProtocols.Tls13, // Force/Explicit TLS 1.3 support
                        CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                    };

                    await sslStream.AuthenticateAsServerAsync(sslOptions, cancellationToken);
                    networkStream = sslStream;

                    _logger.LogInformation("TLS 1.3 Handshake completed successfully on connection {ConnectionId}.", connectionId);
                }

                connection = new TcpConnection(connectionId, tcpClient, networkStream);
                connection.UpdateState(ConnectionLifecycleState.Connecting);

                // Register connection
                _connectionRegistry.Register(connection);
                _logger.LogInformation("TCP connection {ConnectionId} registered in the registry. Active count: {Count}.", connectionId, _connectionRegistry.Count);

                // For Stage 01-04, we don't implement the full authentication protocol.
                // We transition to Active to represent infrastructure state.
                connection.UpdateState(ConnectionLifecycleState.Active);

                // Keep connection open and read from stream until disconnected/canceled
                byte[] buffer = new byte[4096];
                while (!cancellationToken.IsCancellationRequested)
                {
                    int bytesRead = await networkStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                    if (bytesRead == 0)
                    {
                        _logger.LogInformation("TCP client on connection {ConnectionId} disconnected gracefully.", connectionId);
                        break; // EOF reached
                    }

                    // Simulated message parsing (not doing protocol handling yet, just keeping the connection alive)
                    _logger.LogDebug("Received {BytesRead} bytes from connection {ConnectionId}.", bytesRead, connectionId);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Operation canceled for connection {ConnectionId}.", connectionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Connection {ConnectionId} encountered an exception.", connectionId);
            }
            finally
            {
                if (connection != null)
                {
                    _connectionRegistry.Unregister(connectionId);
                    await connection.DisconnectAsync(CancellationToken.None);
                    connection.Dispose();
                }
                else
                {
                    tcpClient.Dispose();
                }

                _logger.LogInformation("TCP connection {ConnectionId} cleaned up and resources released. Active count: {Count}.", connectionId, _connectionRegistry.Count);
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping TCP Server transport foundation...");

            lock (_lock)
            {
                if (_cts != null)
                {
                    try
                    {
                        _cts.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                }

                if (_listener == null)
                {
                    return;
                }

                try
                {
                    _listener.Stop();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while stopping TcpListener.");
                }

                _listener = null;
            }

            // Close and clean up all active connections in the registry
            var activeConnections = _connectionRegistry.GetAll();
            foreach (var connection in activeConnections)
            {
                try
                {
                    _logger.LogInformation("Closing active connection {ConnectionId} during graceful server shutdown.", connection.ConnectionId);
                    _connectionRegistry.Unregister(connection.ConnectionId);
                    await connection.DisconnectAsync(CancellationToken.None);
                    connection.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error closing connection {ConnectionId} during shutdown.", connection.ConnectionId);
                }
            }

            if (_listenerTask != null)
            {
                try
                {
                    await _listenerTask;
                }
                catch
                {
                    // Ignore listener tasks exceptions on cancel
                }
            }

            _cts?.Dispose();
            _serverCertificate?.Dispose();

            _logger.LogInformation("TCP Server transport stopped successfully.");
        }

        public void Dispose()
        {
            _cts?.Dispose();
            _serverCertificate?.Dispose();
        }
    }
}
