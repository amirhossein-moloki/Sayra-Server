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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sayra.Backend.Application.Abstractions.Caching;
using Sayra.Backend.Application.Abstractions.Communication;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Abstractions.Transport;
using Sayra.Backend.Application.Security;
using Sayra.Backend.Application.Workstations;
using Sayra.Backend.Domain;
using Sayra.Backend.Infrastructure.Configuration.Options;

namespace Sayra.Backend.Infrastructure.Transport
{
    public class TcpServer : IHostedService, ITcpServer, IDisposable
    {
        private readonly ITcpConnectionRegistry _connectionRegistry;
        private readonly ITcpSessionManager _sessionManager;
        private readonly ITcpAuthenticationService _tcpAuthenticationService;
        private readonly ICryptographicService _cryptographicService;
        private readonly IRedisService _redisService;
        private readonly ISecureMessageService _secureMessageService;
        private readonly IServiceScopeFactory? _serviceScopeFactory;
        private readonly ServerOptions _serverOptions;
        private readonly TlsOptions _tlsOptions;
        private readonly ILogger<TcpServer> _logger;
        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        private Task? _listenerTask;
        private X509Certificate2? _serverCertificate;
        private readonly object _lock = new();

        public bool IsListening
        {
            get
            {
                lock (_lock)
                {
                    return _listener != null;
                }
            }
        }

        public int ActiveConnectionsCount => _connectionRegistry.Count;

        public TcpServer(
            ITcpConnectionRegistry connectionRegistry,
            ITcpAuthenticationService tcpAuthenticationService,
            ICryptographicService cryptographicService,
            IRedisService redisService,
            ISecureMessageService secureMessageService,
            IOptions<ServerOptions> serverOptions,
            IOptions<TlsOptions> tlsOptions,
            ILogger<TcpServer> logger,
            IServiceScopeFactory? serviceScopeFactory = null,
            ITcpSessionManager? sessionManager = null)
        {
            _connectionRegistry = connectionRegistry ?? throw new ArgumentNullException(nameof(connectionRegistry));
            _tcpAuthenticationService = tcpAuthenticationService ?? throw new ArgumentNullException(nameof(tcpAuthenticationService));
            _cryptographicService = cryptographicService ?? throw new ArgumentNullException(nameof(cryptographicService));
            _redisService = redisService ?? throw new ArgumentNullException(nameof(redisService));
            _secureMessageService = secureMessageService ?? throw new ArgumentNullException(nameof(secureMessageService));
            _serviceScopeFactory = serviceScopeFactory;
            _serverOptions = serverOptions?.Value ?? throw new ArgumentNullException(nameof(serverOptions));
            _tlsOptions = tlsOptions?.Value ?? throw new ArgumentNullException(nameof(tlsOptions));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _sessionManager = sessionManager ?? new TcpSessionManager(_connectionRegistry, _redisService, NullLogger<TcpSessionManager>.Instance);
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting TCP Server transport foundation with Secure Session Management...");

            lock (_lock)
            {
                if (_listener != null)
                {
                    _logger.LogWarning("TCP Server is already running.");
                    return;
                }

                _cts = new CancellationTokenSource();

                LoadCertificate();

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
                    _listener.Start(_serverOptions.Backlog);
                }
                catch (Exception ex)
                {
                    _logger.LogCritical(ex, "Failed to start TcpListener on port {Port}.", _serverOptions.Port);
                    throw;
                }

                _logger.LogInformation("TCP Server successfully listening on {Host}:{Port} with max connections {MaxConn}.", ipAddress, _serverOptions.Port, _serverOptions.MaximumConnections);

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

                    if (_connectionRegistry.Count >= _serverOptions.MaximumConnections)
                    {
                        _logger.LogWarning("Maximum simultaneous connection limit reached ({Limit}). Rejecting connection from {RemoteEndPoint}.",
                            _serverOptions.MaximumConnections, tcpClient.Client.RemoteEndPoint);
                        tcpClient.Close();
                        tcpClient.Dispose();
                        continue;
                    }

                    if (_serverOptions.ReceiveBufferSize > 0)
                    {
                        tcpClient.ReceiveBufferSize = _serverOptions.ReceiveBufferSize;
                    }
                    if (_serverOptions.SendBufferSize > 0)
                    {
                        tcpClient.SendBufferSize = _serverOptions.SendBufferSize;
                    }

                    _logger.LogInformation("New TCP client connection request accepted from {RemoteEndPoint}.", tcpClient.Client.RemoteEndPoint);

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
            string disconnectReason = "Normal Closure";

            try
            {
                string remoteIp = tcpClient.Client?.RemoteEndPoint is IPEndPoint ep ? ep.Address.ToString() : "Unknown";

                if (_serviceScopeFactory != null)
                {
                    try
                    {
                        using var scope = _serviceScopeFactory.CreateScope();
                        var commManager = scope.ServiceProvider.GetService<ICommunicationSessionManager>();
                        if (commManager != null)
                        {
                            await commManager.EstablishSessionAsync(connectionId, remoteIp, null, cancellationToken);
                        }
                    }
                    catch (Exception commEx)
                    {
                        _logger.LogWarning(commEx, "Failed to establish CommunicationSession for connection {ConnectionId}.", connectionId);
                    }
                }

                Stream networkStream = tcpClient.GetStream();

                if (_serverCertificate != null)
                {
                    _logger.LogInformation("Negotiating TLS 1.3 handshake on connection {ConnectionId}...", connectionId);

                    var sslStream = new SslStream(networkStream, false);
                    var sslOptions = new SslServerAuthenticationOptions
                    {
                        ServerCertificate = _serverCertificate,
                        ClientCertificateRequired = _tlsOptions.RequireClientCertificate,
                        EnabledSslProtocols = SslProtocols.Tls13,
                        CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                    };

                    using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    handshakeCts.CancelAfter(TimeSpan.FromSeconds(_serverOptions.HandshakeTimeout));

                    await sslStream.AuthenticateAsServerAsync(sslOptions, handshakeCts.Token);
                    networkStream = sslStream;

                    _logger.LogInformation("TLS 1.3 Handshake completed successfully on connection {ConnectionId}.", connectionId);
                }

                connection = new TcpConnection(connectionId, tcpClient, networkStream);

                // Register session in SessionManager
                await _sessionManager.RegisterSessionAsync(connection, cancellationToken);

                // Perform Handshake & Authentication
                bool authenticated = await _tcpAuthenticationService.AuthenticateAsync(connection, cancellationToken);
                if (!authenticated)
                {
                    disconnectReason = "Authentication Failed";
                    _logger.LogWarning("TCP connection {ConnectionId} failed secure handshake. Closing immediately.", connectionId);
                    return;
                }

                if (!string.IsNullOrEmpty(connection.PcId) && _serviceScopeFactory != null)
                {
                    try
                    {
                        using var scope = _serviceScopeFactory.CreateScope();
                        var commManager = scope.ServiceProvider.GetService<ICommunicationSessionManager>();
                        if (commManager != null)
                        {
                            await commManager.AuthenticateSessionAsync(connectionId, connection.PcId, null, cancellationToken);
                            await commManager.ActivateSessionAsync(connectionId, cancellationToken);
                        }
                    }
                    catch (Exception commEx)
                    {
                        _logger.LogWarning(commEx, "Failed to authenticate CommunicationSession for connection {ConnectionId}.", connectionId);
                    }
                }

                _logger.LogInformation("TCP connection {ConnectionId} successfully authenticated. Entering post-auth message loop.", connectionId);

                var parser = new TcpFrameParser(_serverOptions.MaximumMessageSize);
                byte[] buffer = new byte[8192];

                while (!cancellationToken.IsCancellationRequested)
                {
                    int bytesRead = await networkStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                    if (bytesRead == 0)
                    {
                        disconnectReason = "Client Graceful Disconnect";
                        _logger.LogInformation("TCP client on connection {ConnectionId} disconnected gracefully.", connectionId);
                        break;
                    }

                    parser.Append(buffer, bytesRead);
                    var frames = parser.ExtractFrames();
                    bool shouldClose = false;

                    foreach (var frame in frames)
                    {
                        try
                        {
                            await ProcessSecureMessageAsync(connection, frame, cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            disconnectReason = $"Protocol Error: {ex.Message}";
                            _logger.LogWarning(ex, "Security or protocol violation on connection {ConnectionId}. Terminating connection.", connectionId);
                            shouldClose = true;
                            break;
                        }
                    }

                    if (shouldClose)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                disconnectReason = "Operation Canceled / Timeout / Shutdown";
                _logger.LogInformation("Operation canceled or handshake timed out for connection {ConnectionId}.", connectionId);
            }
            catch (Exception ex)
            {
                disconnectReason = $"Exception: {ex.Message}";
                _logger.LogError(ex, "Connection {ConnectionId} encountered an exception.", connectionId);
            }
            finally
            {
                if (connection != null)
                {
                    try
                    {
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

                    if (_serviceScopeFactory != null)
                    {
                        try
                        {
                            using var scope = _serviceScopeFactory.CreateScope();
                            var commManager = scope.ServiceProvider.GetService<ICommunicationSessionManager>();
                            if (commManager != null)
                            {
                                await commManager.DisconnectSessionAsync(connectionId, disconnectReason, CancellationToken.None);
                            }
                        }
                        catch (Exception commEx)
                        {
                            _logger.LogWarning(commEx, "Failed to disconnect CommunicationSession for connection {ConnectionId}.", connectionId);
                        }
                    }

                    await _sessionManager.HandleDisconnectAsync(connectionId, disconnectReason, CancellationToken.None);
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
            Sayra.Backend.Application.Abstractions.Security.SecureMessageEnvelope? envelope;
            try
            {
                envelope = JsonSerializer.Deserialize<Sayra.Backend.Application.Abstractions.Security.SecureMessageEnvelope>(frame, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Malformed JSON secure message on connection {ConnectionId}.", connection.ConnectionId);
                throw new InvalidOperationException("Malformed envelope JSON.");
            }

            if (envelope == null || string.IsNullOrWhiteSpace(envelope.Payload) || string.IsNullOrWhiteSpace(envelope.Signature) || string.IsNullOrWhiteSpace(envelope.Timestamp))
            {
                _logger.LogWarning("Empty or malformed SecureMessageEnvelope fields on connection {ConnectionId}.", connection.ConnectionId);
                throw new InvalidOperationException("Malformed envelope properties.");
            }

            var session = new ConnectionSession
            {
                ConnectionId = connection.ConnectionId,
                PcId = connection.PcId,
                SessionKey = connection.SessionKey,
                HandshakeState = connection.State
            };

            var appEnvelope = new Sayra.Backend.Application.Security.SecureMessageEnvelope
            {
                Payload = envelope.Payload,
                Signature = envelope.Signature,
                Timestamp = envelope.Timestamp
            };

            var validationResult = await _secureMessageService.HandleSecureMessageAsync(session, appEnvelope);
            if (!validationResult.IsSuccess)
            {
                throw new InvalidOperationException(validationResult.ErrorMessage ?? "Validation failed.");
            }

            _logger.LogDebug("Successfully processed secure message frame from {ConnectionId}.", connection.ConnectionId);

            string? plaintext = validationResult.PlaintextPayload;
            if (!string.IsNullOrEmpty(plaintext))
            {
                using var doc = JsonDocument.Parse(plaintext);
                var root = doc.RootElement;
                if (root.TryGetProperty("type", out var typeProp))
                {
                    string msgType = typeProp.GetString() ?? "";
                    if (msgType.Equals("HEARTBEAT", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("Processing HEARTBEAT for connection {ConnectionId}...", connection.ConnectionId);

                        // Update LastActivity in SessionManager
                        await _sessionManager.UpdateLastActivityAsync(connection.ConnectionId, cancellationToken);

                        if (_serviceScopeFactory != null)
                        {
                            try
                            {
                                using var scope = _serviceScopeFactory.CreateScope();
                                var commManager = scope.ServiceProvider.GetService<ICommunicationSessionManager>();
                                if (commManager != null)
                                {
                                    await commManager.RecordHeartbeatAsync(connection.ConnectionId, DateTime.UtcNow, cancellationToken);
                                }
                            }
                            catch (Exception commEx)
                            {
                                _logger.LogWarning(commEx, "Failed to record heartbeat in CommunicationSession for {ConnectionId}.", connection.ConnectionId);
                            }
                        }

                        // Update workstation's LastSeen in Postgres database
                        if (!string.IsNullOrEmpty(connection.PcId) && _serviceScopeFactory != null)
                        {
                            try
                            {
                                using var scope = _serviceScopeFactory.CreateScope();
                                var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.Persistence.ApplicationDbContext>();
                                var pcIdUpper = connection.PcId.Trim().ToUpperInvariant();
                                var workstation = dbContext.Workstations.FirstOrDefault(w => w.PcId == pcIdUpper);
                                if (workstation != null)
                                {
                                    workstation.LastSeen = DateTime.UtcNow;
                                    workstation.Status = "Online";
                                    await dbContext.SaveChangesAsync(cancellationToken);
                                    _logger.LogDebug("Updated workstation LastSeen in database for PC-ID {PcId}.", connection.PcId);
                                }
                            }
                            catch (Exception dbEx)
                            {
                                _logger.LogWarning(dbEx, "Failed to update workstation LastSeen in DB for {PcId}.", connection.PcId);
                            }
                        }

                        var pongMessage = new Sayra.Backend.Contracts.PongMessage
                        {
                            Type = "PONG",
                            Timestamp = DateTime.UtcNow
                        };

                        await _secureMessageService.SendSecureMessageAsync(session, pongMessage);
                        _logger.LogInformation("Sent PONG back to connection {ConnectionId}.", connection.ConnectionId);
                    }
                    else if (msgType.Equals("SESSION_COMMAND_REQUEST", StringComparison.OrdinalIgnoreCase) && _serviceScopeFactory != null)
                    {
                        _logger.LogInformation("Processing SESSION_COMMAND_REQUEST for connection {ConnectionId}...", connection.ConnectionId);

                        try
                        {
                            if (root.TryGetProperty("payload", out var payloadProp))
                            {
                                var commandPayload = JsonSerializer.Deserialize<Sayra.Backend.Contracts.SessionCommandPayload>(payloadProp.GetRawText(), new JsonSerializerOptions
                                {
                                    PropertyNameCaseInsensitive = true
                                });

                                if (commandPayload != null && !string.IsNullOrEmpty(commandPayload.Action))
                                {
                                    using var scope = _serviceScopeFactory.CreateScope();
                                    var authService = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
                                    var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.Persistence.ApplicationDbContext>();

                                    string actionUpper = commandPayload.Action.Trim().ToUpperInvariant();

                                    var pcIdUpper = connection.PcId?.Trim().ToUpperInvariant() ?? "";
                                    var workstation = dbContext.Workstations.FirstOrDefault(w => w.PcId == pcIdUpper);

                                    var devicePrincipal = new UserPrincipal
                                    {
                                        IsAuthenticated = true,
                                        PcId = connection.PcId,
                                        OrganizationId = workstation?.OrganizationEntityId,
                                        SiteId = workstation?.SiteEntityId,
                                        Roles = new List<string> { RoleCatalog.Gamer },
                                        Permissions = new List<string>
                                        {
                                            PermissionCatalog.StartSession,
                                            PermissionCatalog.StopSession,
                                            PermissionCatalog.PauseSession,
                                            PermissionCatalog.ResumeSession,
                                            PermissionCatalog.ExtendSession
                                        }
                                    };

                                    string requiredPerm = actionUpper switch
                                    {
                                        "START" => PermissionCatalog.StartSession,
                                        "PAUSE" => PermissionCatalog.PauseSession,
                                        "RESUME" => PermissionCatalog.ResumeSession,
                                        "STOP" => PermissionCatalog.StopSession,
                                        "EXTEND" => PermissionCatalog.ExtendSession,
                                        _ => PermissionCatalog.StartSession
                                    };

                                    var authResult = await authService.AuthorizeAsync(devicePrincipal, requiredPerm, workstation, cancellationToken);
                                    if (!authResult.IsAllowed)
                                    {
                                        _logger.LogWarning("TCP Authorization Failure: Connection {ConnectionId} for device {PcId} failed authorization for {Action}: {Reason}",
                                            connection.ConnectionId, connection.PcId, actionUpper, authResult.FailureReason);
                                        return;
                                    }

                                    Sayra.Backend.Contracts.SessionResponseDto? sessionResult = null;

                                    if (actionUpper == "START")
                                    {
                                        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<Sayra.Backend.Application.Sessions.StartSessionCommand, Sayra.Backend.Contracts.SessionResponseDto>>();
                                        var res = await handler.HandleAsync(new Sayra.Backend.Application.Sessions.StartSessionCommand(commandPayload.GamerId, commandPayload.WorkstationId, commandPayload.ReservationId), cancellationToken);
                                        if (res.IsSuccess) sessionResult = res.Value;
                                    }
                                    else if (actionUpper == "PAUSE")
                                    {
                                        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<Sayra.Backend.Application.Sessions.PauseSessionCommand, Sayra.Backend.Contracts.SessionResponseDto>>();
                                        var res = await handler.HandleAsync(new Sayra.Backend.Application.Sessions.PauseSessionCommand(commandPayload.SessionId), cancellationToken);
                                        if (res.IsSuccess) sessionResult = res.Value;
                                    }
                                    else if (actionUpper == "RESUME")
                                    {
                                        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<Sayra.Backend.Application.Sessions.ResumeSessionCommand, Sayra.Backend.Contracts.SessionResponseDto>>();
                                        var res = await handler.HandleAsync(new Sayra.Backend.Application.Sessions.ResumeSessionCommand(commandPayload.SessionId), cancellationToken);
                                        if (res.IsSuccess) sessionResult = res.Value;
                                    }
                                    else if (actionUpper == "STOP")
                                    {
                                        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<Sayra.Backend.Application.Sessions.StopSessionCommand, Sayra.Backend.Contracts.SessionResponseDto>>();
                                        var res = await handler.HandleAsync(new Sayra.Backend.Application.Sessions.StopSessionCommand(commandPayload.SessionId), cancellationToken);
                                        if (res.IsSuccess) sessionResult = res.Value;
                                    }
                                    else if (actionUpper == "EXTEND")
                                    {
                                        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<Sayra.Backend.Application.Sessions.ExtendSessionCommand, Sayra.Backend.Contracts.SessionExtensionResponseDto>>();
                                        await handler.HandleAsync(new Sayra.Backend.Application.Sessions.ExtendSessionCommand(commandPayload.SessionId, commandPayload.AdditionalMinutes ?? 30, commandPayload.IdempotencyKey), cancellationToken);
                                    }

                                    if (sessionResult != null)
                                    {
                                        var updateMsg = new Sayra.Backend.Contracts.SessionStateUpdateMessage
                                        {
                                            MessageType = "SESSION_STATE_UPDATE",
                                            SessionId = sessionResult.SessionId,
                                            GamerId = sessionResult.GamerId,
                                            WorkstationId = sessionResult.WorkstationId,
                                            Status = sessionResult.Status,
                                            ConsumedDuration = TimeSpan.Zero,
                                            RemainingDuration = null,
                                            Timestamp = DateTime.UtcNow
                                        };

                                        await _secureMessageService.SendSecureMessageAsync(session, updateMsg);
                                        _logger.LogInformation("Sent SESSION_STATE_UPDATE back to connection {ConnectionId} for Session {SessionId}.", connection.ConnectionId, sessionResult.SessionId);
                                    }
                                }
                            }
                        }
                        catch (Exception cmdEx)
                        {
                            _logger.LogWarning(cmdEx, "Failed to execute SESSION_COMMAND_REQUEST on connection {ConnectionId}.", connection.ConnectionId);
                        }
                    }
                }
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

            var activeConnections = _connectionRegistry.GetAll();
            foreach (var connection in activeConnections)
            {
                try
                {
                    _logger.LogInformation("Closing active connection {ConnectionId} during graceful server shutdown.", connection.ConnectionId);

                    if (!string.IsNullOrEmpty(connection.PcId) && _serviceScopeFactory != null)
                    {
                        try
                        {
                            using var scope = _serviceScopeFactory.CreateScope();
                            var unbindHandler = scope.ServiceProvider.GetRequiredService<ICommandHandler<UnbindWorkstationConnectionCommand, Workstation?>>();
                            await unbindHandler.HandleAsync(new UnbindWorkstationConnectionCommand
                            {
                                PcId = connection.PcId,
                                ConnectionId = connection.ConnectionId
                            }, CancellationToken.None);
                        }
                        catch (Exception dbEx)
                        {
                            _logger.LogWarning(dbEx, "Failed to unbind workstation state for connection {ConnectionId} during server shutdown.", connection.ConnectionId);
                        }
                    }

                    if (_serviceScopeFactory != null)
                    {
                        try
                        {
                            using var scope = _serviceScopeFactory.CreateScope();
                            var commManager = scope.ServiceProvider.GetService<ICommunicationSessionManager>();
                            if (commManager != null)
                            {
                                await commManager.DisconnectSessionAsync(connection.ConnectionId, "Server Shutdown", CancellationToken.None);
                            }
                        }
                        catch (Exception commEx)
                        {
                            _logger.LogWarning(commEx, "Failed to disconnect CommunicationSession for connection {ConnectionId} during server shutdown.", connection.ConnectionId);
                        }
                    }

                    await _sessionManager.HandleDisconnectAsync(connection.ConnectionId, "Server Shutdown", CancellationToken.None);
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
