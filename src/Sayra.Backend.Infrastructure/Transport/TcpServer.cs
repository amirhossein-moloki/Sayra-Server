using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sayra.Backend.Application.Abstractions.Caching;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Abstractions.Transport;
using Sayra.Backend.Application.Workstations;
using Sayra.Backend.Domain;
using Sayra.Backend.Contracts;
using Sayra.Backend.Infrastructure.Configuration.Options;

namespace Sayra.Backend.Infrastructure.Transport
{
    public class TcpServer : IHostedService, ITcpServer, IDisposable
    {
        private readonly ITcpConnectionRegistry _connectionRegistry;
        private readonly ITcpAuthenticationService _tcpAuthenticationService;
        private readonly ICryptographicService _cryptographicService;
        private readonly IRedisService _redisService;
        private readonly IServiceScopeFactory? _serviceScopeFactory;
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
            ITcpAuthenticationService tcpAuthenticationService,
            ICryptographicService cryptographicService,
            IRedisService redisService,
            IOptions<ServerOptions> serverOptions,
            IOptions<TlsOptions> tlsOptions,
            ILogger<TcpServer> logger,
            IServiceScopeFactory? serviceScopeFactory = null)
        {
            _connectionRegistry = connectionRegistry ?? throw new ArgumentNullException(nameof(connectionRegistry));
            _tcpAuthenticationService = tcpAuthenticationService ?? throw new ArgumentNullException(nameof(tcpAuthenticationService));
            _cryptographicService = cryptographicService ?? throw new ArgumentNullException(nameof(cryptographicService));
            _redisService = redisService ?? throw new ArgumentNullException(nameof(redisService));
            _serviceScopeFactory = serviceScopeFactory;
            _serverOptions = serverOptions?.Value ?? throw new ArgumentNullException(nameof(serverOptions));
            _tlsOptions = tlsOptions?.Value ?? throw new ArgumentNullException(nameof(tlsOptions));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting TCP Server transport foundation with Secure Handshake Handlers...");

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

                connection = new TcpConnection(connectionId, tcpClient, networkStream, _serverOptions.MaxMessageSizeBytes);
                connection.UpdateState(ConnectionLifecycleState.Connecting);

                // Register connection
                _connectionRegistry.Register(connection);
                _logger.LogInformation("TCP connection {ConnectionId} registered in the registry. Active count: {Count}.", connectionId, _connectionRegistry.Count);

                // Perform Handshake & Authentication
                bool authenticated = await _tcpAuthenticationService.AuthenticateAsync(connection, cancellationToken);
                if (!authenticated)
                {
                    _logger.LogWarning("TCP connection {ConnectionId} failed secure handshake. Closing immediately.", connectionId);
                    return;
                }

                _logger.LogInformation("TCP connection {ConnectionId} successfully authenticated. Entering post-auth message loop.", connectionId);

                // Continuous secure message parsing loop using framing layer reader
                while (!cancellationToken.IsCancellationRequested)
                {
                    string? frame;
                    try
                    {
                        frame = await connection.Reader.ReadFrameAsync(cancellationToken);
                    }
                    catch (Sayra.Backend.Domain.Exceptions.ProtocolException ex)
                    {
                        _logger.LogWarning(ex, "Protocol framing error on connection {ConnectionId}. ErrorCode: {ErrorCode}. Closing connection.", connectionId, ex.ErrorCode);
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error reading frame from connection {ConnectionId}. Closing connection.", connectionId);
                        break;
                    }

                    if (frame == null)
                    {
                        _logger.LogInformation("TCP client on connection {ConnectionId} disconnected gracefully.", connectionId);
                        break; // EOF reached
                    }

                    try
                    {
                        await ProcessSecureMessageAsync(connection, frame, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Security or protocol violation on connection {ConnectionId}. Terminating connection.", connectionId);
                        break;
                    }
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

                    // Clean up connection metadata from Redis cache and database workstation status
                    try
                    {
                        if (Guid.TryParse(connectionId, out var connectionGuid))
                        {
                            var redisKey = RedisKeyGenerator.ConnectionStateKey(connectionGuid);
                            await _redisService.RemoveAsync(redisKey);
                        }

                        if (!string.IsNullOrEmpty(connection.PcId) && _serviceScopeFactory != null)
                        {
                            using var scope = _serviceScopeFactory.CreateScope();
                            var unbindHandler = scope.ServiceProvider.GetRequiredService<ICommandHandler<UnbindWorkstationConnectionCommand, Workstation?>>();
                            await unbindHandler.HandleAsync(new UnbindWorkstationConnectionCommand
                            {
                                PcId = connection.PcId,
                                ConnectionId = connectionId
                            }, CancellationToken.None);
                        }
                    }
                    catch (Exception dbEx)
                    {
                        _logger.LogWarning(dbEx, "Failed to unbind workstation state for connection {ConnectionId} during disconnect.", connectionId);
                    }

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

        private async Task ProcessSecureMessageAsync(ITcpConnection connection, string frame, CancellationToken cancellationToken)
        {
            // Parse SecureMessageEnvelope
            Sayra.Backend.Contracts.SecureMessageEnvelope? envelope;
            try
            {
                envelope = JsonSerializer.Deserialize<Sayra.Backend.Contracts.SecureMessageEnvelope>(frame, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Malformed JSON secure envelope. ConnectionId: {ConnectionId}, PcId: {PcId}, ErrorCode: {ErrorCode}, FrameSize: {FrameSize}",
                    connection.ConnectionId,
                    connection.PcId,
                    Sayra.Backend.Domain.Exceptions.ProtocolException.InvalidJson,
                    frame.Length);
                throw new Sayra.Backend.Domain.Exceptions.ProtocolException(Sayra.Backend.Domain.Exceptions.ProtocolException.InvalidJson, "Malformed envelope JSON.");
            }

            if (envelope == null || string.IsNullOrWhiteSpace(envelope.Payload) || string.IsNullOrWhiteSpace(envelope.Signature) || string.IsNullOrWhiteSpace(envelope.Timestamp))
            {
                _logger.LogWarning(
                    "Malformed secure envelope properties. ConnectionId: {ConnectionId}, PcId: {PcId}, ErrorCode: {ErrorCode}, FrameSize: {FrameSize}",
                    connection.ConnectionId,
                    connection.PcId,
                    Sayra.Backend.Domain.Exceptions.ProtocolException.InvalidMessage,
                    frame.Length);
                throw new Sayra.Backend.Domain.Exceptions.ProtocolException(Sayra.Backend.Domain.Exceptions.ProtocolException.InvalidMessage, "Malformed envelope properties.");
            }

            // 1. Verify Timestamp Freshness
            if (!DateTime.TryParse(envelope.Timestamp, out var timestamp))
            {
                _logger.LogWarning(
                    "Invalid envelope timestamp format. ConnectionId: {ConnectionId}, PcId: {PcId}, ErrorCode: {ErrorCode}, Timestamp: {Timestamp}",
                    connection.ConnectionId,
                    connection.PcId,
                    Sayra.Backend.Domain.Exceptions.ProtocolException.ProtocolError,
                    envelope.Timestamp);
                throw new Sayra.Backend.Domain.Exceptions.ProtocolException(Sayra.Backend.Domain.Exceptions.ProtocolException.ProtocolError, "Invalid timestamp format.");
            }

            var now = DateTime.UtcNow;
            var drift = Math.Abs((now - timestamp.ToUniversalTime()).TotalSeconds);
            if (drift > 300)
            {
                _logger.LogWarning(
                    "Envelope timestamp drift of {Drift}s exceeded 300s window. ConnectionId: {ConnectionId}, PcId: {PcId}, ErrorCode: {ErrorCode}",
                    drift,
                    connection.ConnectionId,
                    connection.PcId,
                    Sayra.Backend.Domain.Exceptions.ProtocolException.ProtocolError);
                throw new Sayra.Backend.Domain.Exceptions.ProtocolException(Sayra.Backend.Domain.Exceptions.ProtocolException.ProtocolError, "Timestamp drift exceeded limit.");
            }

            // 2. Validate HMAC-SHA256 Signature using session key
            if (connection.SessionKey == null)
            {
                _logger.LogWarning("Connection {ConnectionId} has no negotiated SessionKey.", connection.ConnectionId);
                throw new Sayra.Backend.Domain.Exceptions.ProtocolException(Sayra.Backend.Domain.Exceptions.ProtocolException.ProtocolError, "Unauthenticated secure message.");
            }

            string signatureInput = envelope.Payload + "|" + envelope.Timestamp;
            byte[] signatureInputBytes = Encoding.UTF8.GetBytes(signatureInput);
            byte[] expectedSignature = _cryptographicService.ComputeHmacSha256(signatureInputBytes, connection.SessionKey);

            byte[] clientSignatureBytes;
            try
            {
                clientSignatureBytes = Convert.FromBase64String(envelope.Signature);
            }
            catch (FormatException)
            {
                _logger.LogWarning(
                    "Invalid Base64 signature field. ConnectionId: {ConnectionId}, PcId: {PcId}, ErrorCode: {ErrorCode}",
                    connection.ConnectionId,
                    connection.PcId,
                    Sayra.Backend.Domain.Exceptions.ProtocolException.ProtocolError);
                throw new Sayra.Backend.Domain.Exceptions.ProtocolException(Sayra.Backend.Domain.Exceptions.ProtocolException.ProtocolError, "Invalid signature Base64.");
            }

            if (!CryptographicOperations.FixedTimeEquals(expectedSignature, clientSignatureBytes))
            {
                _logger.LogWarning(
                    "Envelope signature mismatch. ConnectionId: {ConnectionId}, PcId: {PcId}, ErrorCode: {ErrorCode}",
                    connection.ConnectionId,
                    connection.PcId,
                    Sayra.Backend.Domain.Exceptions.ProtocolException.ProtocolError);
                throw new Sayra.Backend.Domain.Exceptions.ProtocolException(Sayra.Backend.Domain.Exceptions.ProtocolException.ProtocolError, "Signature mismatch.");
            }

            // 3. Decrypt ciphertext payload using session key
            byte[] rawPayloadBytes;
            try
            {
                rawPayloadBytes = Convert.FromBase64String(envelope.Payload);
            }
            catch (FormatException)
            {
                _logger.LogWarning(
                    "Invalid Base64 payload field. ConnectionId: {ConnectionId}, PcId: {PcId}, ErrorCode: {ErrorCode}",
                    connection.ConnectionId,
                    connection.PcId,
                    Sayra.Backend.Domain.Exceptions.ProtocolException.ProtocolError);
                throw new Sayra.Backend.Domain.Exceptions.ProtocolException(Sayra.Backend.Domain.Exceptions.ProtocolException.ProtocolError, "Invalid payload Base64.");
            }

            if (rawPayloadBytes.Length < 16)
            {
                _logger.LogWarning(
                    "Payload payload too short. ConnectionId: {ConnectionId}, PcId: {PcId}, ErrorCode: {ErrorCode}",
                    connection.ConnectionId,
                    connection.PcId,
                    Sayra.Backend.Domain.Exceptions.ProtocolException.ProtocolError);
                throw new Sayra.Backend.Domain.Exceptions.ProtocolException(Sayra.Backend.Domain.Exceptions.ProtocolException.ProtocolError, "Payload too short.");
            }

            byte[] iv = new byte[16];
            byte[] ciphertext = new byte[rawPayloadBytes.Length - 16];
            Buffer.BlockCopy(rawPayloadBytes, 0, iv, 0, 16);
            Buffer.BlockCopy(rawPayloadBytes, 16, ciphertext, 0, ciphertext.Length);

            byte[] decryptedBytes;
            try
            {
                decryptedBytes = _cryptographicService.DecryptAes256Cbc(ciphertext, connection.SessionKey, iv);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Payload decryption failure. ConnectionId: {ConnectionId}, PcId: {PcId}, ErrorCode: {ErrorCode}",
                    connection.ConnectionId,
                    connection.PcId,
                    Sayra.Backend.Domain.Exceptions.ProtocolException.ProtocolError);
                throw new Sayra.Backend.Domain.Exceptions.ProtocolException(Sayra.Backend.Domain.Exceptions.ProtocolException.ProtocolError, "Decryption failed.");
            }

            string decryptedPayload = Encoding.UTF8.GetString(decryptedBytes);
            int frameSize = decryptedPayload.Length;

            object? resolvedContract = null;
            string? messageType = null;
            string? correlationId = null;

            try
            {
                resolvedContract = Sayra.Backend.Application.Transport.ProtocolMessageResolver.ResolveAndDeserialize(decryptedPayload);
                messageType = resolvedContract?.GetType().Name;

                if (resolvedContract != null)
                {
                    var corrProp = resolvedContract.GetType().GetProperty("CorrelationId");
                    if (corrProp != null)
                    {
                        correlationId = corrProp.GetValue(resolvedContract) as string;
                    }
                }

                _logger.LogInformation(
                    "Processed secure message. ConnectionId: {ConnectionId}, PcId: {PcId}, MessageType: {MessageType}, FrameSize: {FrameSize}, CorrelationId: {CorrelationId}",
                    connection.ConnectionId,
                    connection.PcId,
                    messageType,
                    frameSize,
                    correlationId);
            }
            catch (Sayra.Backend.Domain.Exceptions.ProtocolException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Protocol or serialization error on secure message. ConnectionId: {ConnectionId}, PcId: {PcId}, ErrorCode: {ErrorCode}, MessageType: {MessageType}, FrameSize: {FrameSize}",
                    connection.ConnectionId,
                    connection.PcId,
                    ex.ErrorCode,
                    messageType ?? "UNKNOWN",
                    frameSize);
                throw;
            }

            await Task.CompletedTask;
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

                    try
                    {
                        if (Guid.TryParse(connection.ConnectionId, out var connectionGuid))
                        {
                            var redisKey = RedisKeyGenerator.ConnectionStateKey(connectionGuid);
                            await _redisService.RemoveAsync(redisKey);
                        }

                        if (!string.IsNullOrEmpty(connection.PcId) && _serviceScopeFactory != null)
                        {
                            using var scope = _serviceScopeFactory.CreateScope();
                            var unbindHandler = scope.ServiceProvider.GetRequiredService<ICommandHandler<UnbindWorkstationConnectionCommand, Workstation?>>();
                            await unbindHandler.HandleAsync(new UnbindWorkstationConnectionCommand
                            {
                                PcId = connection.PcId,
                                ConnectionId = connection.ConnectionId
                            }, CancellationToken.None);
                        }
                    }
                    catch (Exception redisEx)
                    {
                        _logger.LogWarning(redisEx, "Failed to clean up Redis state for connection {ConnectionId} during server shutdown.", connection.ConnectionId);
                    }

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
