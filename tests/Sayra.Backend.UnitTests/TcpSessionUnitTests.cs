using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sayra.Backend.Application.Abstractions.Caching;
using Sayra.Backend.Application.Abstractions.Transport;
using Sayra.Backend.Domain;
using Sayra.Backend.Infrastructure.Transport;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class TcpSessionUnitTests
    {
        private readonly Mock<IRedisService> _redisMock;

        public TcpSessionUnitTests()
        {
            _redisMock = new Mock<IRedisService>();
        }

        [Fact]
        public void StateTransition_ValidSequence_Succeeds()
        {
            using var tcpClient = new TcpClient();
            using var ms = new MemoryStream();
            var connection = new TcpConnection("conn-1", tcpClient, ms);

            Assert.Equal(ConnectionLifecycleState.Connecting, connection.State);

            connection.UpdateState(ConnectionLifecycleState.Authenticating);
            Assert.Equal(ConnectionLifecycleState.Authenticating, connection.State);

            connection.UpdateState(ConnectionLifecycleState.Authenticated);
            Assert.Equal(ConnectionLifecycleState.Authenticated, connection.State);

            connection.UpdateState(ConnectionLifecycleState.Active);
            Assert.Equal(ConnectionLifecycleState.Active, connection.State);

            connection.UpdateState(ConnectionLifecycleState.Disconnected);
            Assert.Equal(ConnectionLifecycleState.Disconnected, connection.State);
        }

        [Theory]
        [InlineData(ConnectionLifecycleState.Disconnected, ConnectionLifecycleState.Active)]
        [InlineData(ConnectionLifecycleState.Disconnected, ConnectionLifecycleState.Connecting)]
        [InlineData(ConnectionLifecycleState.Active, ConnectionLifecycleState.Connecting)]
        [InlineData(ConnectionLifecycleState.Active, ConnectionLifecycleState.Authenticating)]
        public void StateTransition_InvalidTransition_ThrowsInvalidOperationException(
            ConnectionLifecycleState initialState, ConnectionLifecycleState invalidNextState)
        {
            using var tcpClient = new TcpClient();
            using var ms = new MemoryStream();
            var connection = new TcpConnection("conn-1", tcpClient, ms);

            if (initialState != ConnectionLifecycleState.Connecting)
            {
                if (initialState == ConnectionLifecycleState.Disconnected)
                {
                    connection.UpdateState(ConnectionLifecycleState.Disconnected);
                }
                else if (initialState == ConnectionLifecycleState.Active)
                {
                    connection.UpdateState(ConnectionLifecycleState.Authenticating);
                    connection.UpdateState(ConnectionLifecycleState.Authenticated);
                    connection.UpdateState(ConnectionLifecycleState.Active);
                }
            }

            Assert.Throws<InvalidOperationException>(() => connection.UpdateState(invalidNextState));
        }

        [Fact]
        public void ConnectionContext_FromConnection_MapsAllPropertiesCorrectly()
        {
            using var tcpClient = new TcpClient();
            using var ms = new MemoryStream();
            var connection = new TcpConnection("conn-100", tcpClient, ms)
            {
                PcId = "PC-ALPHA",
                Hostname = "HOST-01",
                SessionKey = new byte[] { 1, 2, 3 }
            };

            var context = TcpConnectionContext.FromConnection(connection);

            Assert.Equal("conn-100", context.ConnectionId);
            Assert.Equal("PC-ALPHA", context.PcId);
            Assert.Equal("HOST-01", context.Hostname);
            Assert.Equal(new byte[] { 1, 2, 3 }, context.SessionKey);
            Assert.Equal(ConnectionLifecycleState.Connecting, context.ConnectionState);
        }

        [Fact]
        public void ConnectionRegistry_RegisterAndGet_OperatesCorrectly()
        {
            var registry = new TcpConnectionRegistry();
            using var tcpClient = new TcpClient();
            using var ms = new MemoryStream();
            var conn = new TcpConnection("conn-1", tcpClient, ms) { PcId = "PC-001" };

            registry.Register(conn);

            Assert.Equal(1, registry.Count);
            Assert.Same(conn, registry.Get("conn-1"));
            Assert.Same(conn, registry.GetByPcId("PC-001"));
            Assert.Same(conn, registry.GetByPcId("pc-001")); // Case-insensitive lookup

            registry.Unregister("conn-1");
            Assert.Equal(0, registry.Count);
            Assert.Null(registry.Get("conn-1"));
            Assert.Null(registry.GetByPcId("PC-001"));
        }

        [Fact]
        public void ConnectionRegistry_ConcurrentOperations_ThreadSafe()
        {
            var registry = new TcpConnectionRegistry();
            int itemCount = 1000;

            Parallel.For(0, itemCount, i =>
            {
                using var tcpClient = new TcpClient();
                using var ms = new MemoryStream();
                var conn = new TcpConnection($"conn-{i}", tcpClient, ms) { PcId = $"PC-{i}" };
                registry.Register(conn);
            });

            Assert.Equal(itemCount, registry.Count);

            Parallel.For(0, itemCount, i =>
            {
                var conn = registry.Get($"conn-{i}");
                Assert.NotNull(conn);
                var pcConn = registry.GetByPcId($"PC-{i}");
                Assert.NotNull(pcConn);
            });

            Parallel.For(0, itemCount, i =>
            {
                registry.Unregister($"conn-{i}");
            });

            Assert.Equal(0, registry.Count);
        }

        [Fact]
        public async Task SessionManager_LifecycleEventsAndStateSync_ExecutesCorrectly()
        {
            var registry = new TcpConnectionRegistry();
            var sessionManager = new TcpSessionManager(registry, _redisMock.Object, NullLogger<TcpSessionManager>.Instance);

            string connId = Guid.NewGuid().ToString();
            using var tcpClient = new TcpClient();
            using var ms = new MemoryStream();
            var conn = new TcpConnection(connId, tcpClient, ms) { PcId = "PC-SESSION-1" };

            // 1. Register
            await sessionManager.RegisterSessionAsync(conn);
            Assert.Equal(1, registry.Count);

            var ctx = sessionManager.GetSessionContext(connId);
            Assert.NotNull(ctx);
            Assert.Equal(ConnectionLifecycleState.Connecting, ctx.ConnectionState);

            // 2. Transition State
            await sessionManager.TransitionStateAsync(connId, ConnectionLifecycleState.Authenticating);
            Assert.Equal(ConnectionLifecycleState.Authenticating, conn.State);

            await sessionManager.TransitionStateAsync(connId, ConnectionLifecycleState.Active);
            Assert.Equal(ConnectionLifecycleState.Active, conn.State);

            // Verify Redis Sync called for Active state
            _redisMock.Verify(r => r.SetAsync(It.IsAny<string>(), It.IsAny<ConnectionStateMetadata>(), It.IsAny<TimeSpan?>(), default), Times.AtLeastOnce);

            // 3. Update Activity
            await sessionManager.UpdateLastActivityAsync(connId);

            // 4. Disconnect
            await sessionManager.HandleDisconnectAsync(connId, "Test Disconnect");

            Assert.Equal(0, registry.Count);
            Assert.Equal(ConnectionLifecycleState.Disconnected, conn.State);
            _redisMock.Verify(r => r.RemoveAsync(It.IsAny<string>(), default), Times.AtLeastOnce);
        }
    }
}
