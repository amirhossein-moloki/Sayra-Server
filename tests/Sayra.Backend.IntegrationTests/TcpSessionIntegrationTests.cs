using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Sayra.Backend.Api;
using Sayra.Backend.Application.Abstractions.Caching;
using Sayra.Backend.Application.Abstractions.Transport;
using Sayra.Backend.Domain;
using Sayra.Backend.Infrastructure.Transport;
using Xunit;

namespace Sayra.Backend.IntegrationTests
{
    public class TcpSessionIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly ITcpSessionManager _sessionManager;
        private readonly ITcpConnectionRegistry _connectionRegistry;
        private readonly IRedisService _redisService;

        public TcpSessionIntegrationTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
            _sessionManager = factory.Services.GetRequiredService<ITcpSessionManager>();
            _connectionRegistry = factory.Services.GetRequiredService<ITcpConnectionRegistry>();
            _redisService = factory.Services.GetRequiredService<IRedisService>();
        }

        [Fact]
        public async Task TcpSession_Lifecycle_Register_StateTransition_And_DisconnectCleanup()
        {
            using var tcpClient = new TcpClient();
            using var stream = new MemoryStream();
            string connectionId = Guid.NewGuid().ToString();

            var connection = new TcpConnection(connectionId, tcpClient, stream)
            {
                PcId = "PC-INTEG-01",
                Hostname = "STATION-01"
            };

            // 1. Register Session
            await _sessionManager.RegisterSessionAsync(connection);

            Assert.Equal(ConnectionLifecycleState.Connecting, connection.State);
            var ctx = _sessionManager.GetSessionContext(connectionId);
            Assert.NotNull(ctx);
            Assert.Equal("PC-INTEG-01", ctx.PcId);
            Assert.Equal("STATION-01", ctx.Hostname);

            // 2. Transition State to Authenticating then Active
            await _sessionManager.TransitionStateAsync(connectionId, ConnectionLifecycleState.Authenticating);
            Assert.Equal(ConnectionLifecycleState.Authenticating, connection.State);

            await _sessionManager.TransitionStateAsync(connectionId, ConnectionLifecycleState.Active);
            Assert.Equal(ConnectionLifecycleState.Active, connection.State);

            // Verify active session list
            var activeSessions = _sessionManager.GetAllActiveSessions();
            Assert.Contains(activeSessions, s => s.ConnectionId == connectionId);

            // 3. Verify Redis cache if Redis is available
            bool isRedisOnline = await _redisService.PingAsync();
            if (isRedisOnline)
            {
                var redisKey = RedisKeyGenerator.ConnectionStateKey(Guid.Parse(connectionId));
                var cachedState = await _redisService.GetAsync<ConnectionStateMetadata>(redisKey);
                Assert.NotNull(cachedState);
                Assert.Equal("Active", cachedState.State);
                Assert.Equal("PC-INTEG-01", cachedState.PcId);
            }

            // 4. Handle Disconnect
            await _sessionManager.HandleDisconnectAsync(connectionId, "Client Terminated");

            Assert.Equal(ConnectionLifecycleState.Disconnected, connection.State);
            Assert.Null(_connectionRegistry.Get(connectionId));
            Assert.Null(_sessionManager.GetSessionContext(connectionId));

            if (isRedisOnline)
            {
                var redisKey = RedisKeyGenerator.ConnectionStateKey(Guid.Parse(connectionId));
                var removedState = await _redisService.GetAsync<ConnectionStateMetadata>(redisKey);
                Assert.Null(removedState);
            }
        }

        [Fact]
        public async Task TcpSession_StressTest_MultipleConcurrentClients()
        {
            int clientCount = 50;
            var connectionIds = new List<string>();

            // 1. Concurrent Registration & Transition to Active
            var tasks = Enumerable.Range(0, clientCount).Select(async i =>
            {
                using var tcpClient = new TcpClient();
                using var stream = new MemoryStream();
                string connId = Guid.NewGuid().ToString();

                lock (connectionIds)
                {
                    connectionIds.Add(connId);
                }

                var conn = new TcpConnection(connId, tcpClient, stream)
                {
                    PcId = $"PC-STRESS-{i:D3}",
                    Hostname = $"HOST-STRESS-{i:D3}"
                };

                await _sessionManager.RegisterSessionAsync(conn);
                await _sessionManager.TransitionStateAsync(connId, ConnectionLifecycleState.Authenticating);
                await _sessionManager.TransitionStateAsync(connId, ConnectionLifecycleState.Active);
            });

            await Task.WhenAll(tasks);

            // 2. Query Registry and Active Sessions
            var allActive = _sessionManager.GetAllActiveSessions();
            Assert.True(allActive.Count >= clientCount);

            // 3. Concurrent Disconnect
            var disconnectTasks = connectionIds.Select(async id =>
            {
                await _sessionManager.HandleDisconnectAsync(id, "Stress Test Cleanup");
            });

            await Task.WhenAll(disconnectTasks);

            // Verify all stress test connections cleaned up
            foreach (var id in connectionIds)
            {
                Assert.Null(_sessionManager.GetSessionContext(id));
            }
        }
    }
}
