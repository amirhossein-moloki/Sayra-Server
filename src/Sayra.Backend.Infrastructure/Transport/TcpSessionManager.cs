using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Sayra.Backend.Application.Abstractions.Caching;
using Sayra.Backend.Application.Abstractions.Transport;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Events;
using Sayra.Backend.Infrastructure.Security;

namespace Sayra.Backend.Infrastructure.Transport
{
    public class TcpSessionManager : ITcpSessionManager
    {
        private readonly ITcpConnectionRegistry _connectionRegistry;
        private readonly IRedisService _redisService;
        private readonly ILogger<TcpSessionManager> _logger;

        public TcpSessionManager(
            ITcpConnectionRegistry connectionRegistry,
            IRedisService redisService,
            ILogger<TcpSessionManager> logger)
        {
            _connectionRegistry = connectionRegistry ?? throw new ArgumentNullException(nameof(connectionRegistry));
            _redisService = redisService ?? throw new ArgumentNullException(nameof(redisService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task RegisterSessionAsync(ITcpConnection connection, CancellationToken cancellationToken = default)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            _connectionRegistry.Register(connection);

            var timestamp = DateTime.UtcNow;
            _logger.LogInformation(
                "Client connected. ConnectionId: {ConnectionId}, RemoteIpAddress: {IPAddress}, Timestamp: {Timestamp}",
                connection.ConnectionId,
                connection.RemoteIpAddress ?? "Unknown",
                timestamp);

            var connectedEvent = new ClientConnectedEvent(
                connection.ConnectionId,
                connection.RemoteIpAddress ?? "Unknown",
                timestamp);

            await Task.CompletedTask;
        }

        public async Task TransitionStateAsync(string connectionId, ConnectionLifecycleState newState, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(connectionId)) return;

            var connection = _connectionRegistry.Get(connectionId);
            if (connection == null)
            {
                _logger.LogWarning("Attempted state transition for unknown connection {ConnectionId}.", connectionId);
                return;
            }

            await TransitionStateAsync(connection, newState, cancellationToken);
        }

        public async Task TransitionStateAsync(ITcpConnection connection, ConnectionLifecycleState newState, CancellationToken cancellationToken = default)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            var oldState = connection.State;
            if (oldState == newState) return;

            connection.UpdateState(newState);
            var timestamp = DateTime.UtcNow;

            _logger.LogInformation(
                "Connection state changed. ConnectionId: {ConnectionId}, PcId: {PcId}, OldState: {OldState}, NewState: {NewState}, Timestamp: {Timestamp}",
                connection.ConnectionId,
                connection.PcId ?? "N/A",
                oldState,
                newState,
                timestamp);

            var stateChangedEvent = new ClientConnectionStateChangedEvent(
                connection.ConnectionId,
                connection.PcId,
                oldState.ToString(),
                newState.ToString(),
                timestamp);

            // Sync with Redis if authenticated or active
            if (newState is ConnectionLifecycleState.Authenticated or ConnectionLifecycleState.Active)
            {
                await SyncRedisSessionAsync(connection, cancellationToken);
            }
            else if (newState == ConnectionLifecycleState.Disconnected)
            {
                await RemoveRedisSessionAsync(connection.ConnectionId, cancellationToken);
            }
        }

        public async Task UpdateLastActivityAsync(string connectionId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(connectionId)) return;

            var connection = _connectionRegistry.Get(connectionId);
            if (connection != null)
            {
                connection.LastActivity = DateTime.UtcNow;
                if (connection.State is ConnectionLifecycleState.Authenticated or ConnectionLifecycleState.Active)
                {
                    await SyncRedisSessionAsync(connection, cancellationToken);
                }
            }
        }

        public async Task HandleDisconnectAsync(string connectionId, string reason = "Normal Closure", CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(connectionId)) return;

            var connection = _connectionRegistry.Get(connectionId);
            if (connection == null)
            {
                _logger.LogDebug("Disconnect requested for untracked connection {ConnectionId}.", connectionId);
                return;
            }

            string? pcId = connection.PcId;
            string remoteIp = connection.RemoteIpAddress ?? "Unknown";
            var timestamp = DateTime.UtcNow;

            try
            {
                connection.UpdateState(ConnectionLifecycleState.Disconnected);
            }
            catch
            {
                // Ignore state transition error if already disconnected
            }

            _connectionRegistry.Unregister(connectionId);

            await RemoveRedisSessionAsync(connectionId, cancellationToken);

            try
            {
                await connection.DisconnectAsync(cancellationToken);
                connection.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while closing connection resources for {ConnectionId}.", connectionId);
            }

            _logger.LogInformation(
                "Client disconnected. ConnectionId: {ConnectionId}, PcId: {PcId}, RemoteIpAddress: {IPAddress}, Reason: {Reason}, Timestamp: {Timestamp}",
                connectionId,
                pcId ?? "N/A",
                remoteIp,
                reason,
                timestamp);

            var disconnectedEvent = new ClientDisconnectedEvent(
                connectionId,
                pcId,
                remoteIp,
                reason,
                timestamp);
        }

        public TcpConnectionContext? GetSessionContext(string connectionId)
        {
            if (string.IsNullOrEmpty(connectionId)) return null;
            var connection = _connectionRegistry.Get(connectionId);
            return connection != null ? TcpConnectionContext.FromConnection(connection) : null;
        }

        public TcpConnectionContext? GetSessionContextByPcId(string pcId)
        {
            if (string.IsNullOrWhiteSpace(pcId)) return null;
            var connection = _connectionRegistry.GetByPcId(pcId);
            return connection != null ? TcpConnectionContext.FromConnection(connection) : null;
        }

        public IReadOnlyCollection<TcpConnectionContext> GetAllActiveSessions()
        {
            return _connectionRegistry.GetAll()
                .Select(TcpConnectionContext.FromConnection)
                .ToList();
        }

        private async Task SyncRedisSessionAsync(ITcpConnection connection, CancellationToken cancellationToken)
        {
            try
            {
                if (Guid.TryParse(connection.ConnectionId, out var connectionGuid))
                {
                    var redisKey = RedisKeyGenerator.ConnectionStateKey(connectionGuid);
                    var metadata = new ConnectionStateMetadata
                    {
                        ConnectionId = connection.ConnectionId,
                        State = connection.State.ToString(),
                        PcId = connection.PcId,
                        Hostname = connection.Hostname,
                        SiteId = connection.SiteId,
                        ClientVersion = connection.ClientVersion,
                        AuthenticatedAt = connection.ConnectedAt,
                        ConnectedAt = connection.ConnectedAt,
                        LastActivity = connection.LastActivity
                    };
                    await _redisService.SetAsync(redisKey, metadata, TimeSpan.FromHours(24));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to sync Redis session metadata for connection {ConnectionId}.", connection.ConnectionId);
            }
        }

        private async Task RemoveRedisSessionAsync(string connectionId, CancellationToken cancellationToken)
        {
            try
            {
                if (Guid.TryParse(connectionId, out var connectionGuid))
                {
                    var redisKey = RedisKeyGenerator.ConnectionStateKey(connectionGuid);
                    await _redisService.RemoveAsync(redisKey);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to remove Redis session metadata for connection {ConnectionId}.", connectionId);
            }
        }
    }
}
