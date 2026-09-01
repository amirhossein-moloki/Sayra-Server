using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sayra.Backend.Application.Abstractions.Caching;
using Sayra.Backend.Application.Abstractions.Communication;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Abstractions.Transport;
using Sayra.Backend.Application.Communication;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Domain.Events;
using Sayra.Backend.Domain.ValueObjects;
using Sayra.Backend.Infrastructure.Configuration;
using Sayra.Backend.Infrastructure.Configuration.Options;
using Sayra.Backend.Infrastructure.Persistence;
using Sayra.Backend.Infrastructure.Transport;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class HeartbeatLivenessTests
    {
        [Fact]
        public void ConfigurationValidator_ServerOptions_HeartbeatValidation_PassesValidConfig()
        {
            var dbOpt = new DatabaseOptions { ConnectionString = "Host=localhost;Database=test;Username=postgres;Password=postgres" };
            var redisOpt = new RedisOptions { ConnectionString = "localhost:6379" };
            var discOpt = new DiscoveryOptions { UdpPort = 37020 };
            var serverOpt = new ServerOptions
            {
                Port = 5000,
                HandshakeTimeout = 15,
                ConnectionTimeout = 300,
                MaximumConnections = 1000,
                ReceiveBufferSize = 8192,
                SendBufferSize = 8192,
                MaximumMessageSize = 65536,
                HeartbeatInterval = 30,
                HeartbeatTimeout = 90,
                HeartbeatGracePeriod = 15,
                LivenessCheckInterval = 15
            };

            // Should not throw
            ConfigurationValidator.Validate(dbOpt, redisOpt, serverOpt, discOpt);
        }

        [Fact]
        public void ConfigurationValidator_ServerOptions_InvalidHeartbeatInterval_ThrowsInvalidOperationException()
        {
            var dbOpt = new DatabaseOptions { ConnectionString = "Host=localhost;Database=test;Username=postgres;Password=postgres" };
            var redisOpt = new RedisOptions { ConnectionString = "localhost:6379" };
            var discOpt = new DiscoveryOptions { UdpPort = 37020 };
            var serverOpt = new ServerOptions { HeartbeatInterval = 0 };

            Assert.Throws<InvalidOperationException>(() => ConfigurationValidator.Validate(dbOpt, redisOpt, serverOpt, discOpt));
        }

        [Fact]
        public void ConfigurationValidator_ServerOptions_HeartbeatTimeoutLessThanInterval_ThrowsInvalidOperationException()
        {
            var dbOpt = new DatabaseOptions { ConnectionString = "Host=localhost;Database=test;Username=postgres;Password=postgres" };
            var redisOpt = new RedisOptions { ConnectionString = "localhost:6379" };
            var discOpt = new DiscoveryOptions { UdpPort = 37020 };
            var serverOpt = new ServerOptions
            {
                HeartbeatInterval = 30,
                HeartbeatTimeout = 20
            };

            Assert.Throws<InvalidOperationException>(() => ConfigurationValidator.Validate(dbOpt, redisOpt, serverOpt, discOpt));
        }

        [Fact]
        public void ConfigurationValidator_ServerOptions_NegativeGracePeriod_ThrowsInvalidOperationException()
        {
            var dbOpt = new DatabaseOptions { ConnectionString = "Host=localhost;Database=test;Username=postgres;Password=postgres" };
            var redisOpt = new RedisOptions { ConnectionString = "localhost:6379" };
            var discOpt = new DiscoveryOptions { UdpPort = 37020 };
            var serverOpt = new ServerOptions
            {
                HeartbeatGracePeriod = -1
            };

            Assert.Throws<InvalidOperationException>(() => ConfigurationValidator.Validate(dbOpt, redisOpt, serverOpt, discOpt));
        }

        [Fact]
        public void ConfigurationValidator_ServerOptions_InvalidLivenessCheckInterval_ThrowsInvalidOperationException()
        {
            var dbOpt = new DatabaseOptions { ConnectionString = "Host=localhost;Database=test;Username=postgres;Password=postgres" };
            var redisOpt = new RedisOptions { ConnectionString = "localhost:6379" };
            var discOpt = new DiscoveryOptions { UdpPort = 37020 };
            var serverOpt = new ServerOptions
            {
                LivenessCheckInterval = 0
            };

            Assert.Throws<InvalidOperationException>(() => ConfigurationValidator.Validate(dbOpt, redisOpt, serverOpt, discOpt));
        }

        [Fact]
        public async Task ProcessHeartbeatCommandHandler_RecordsHeartbeat_And_EmitsEvent()
        {
            var connId = "CONN-HB-CMD-1";
            var session = CommunicationSession.Create(connId);
            session.Authenticate("PC-HB-1");
            session.Activate();

            var repo = new FakeCommunicationSessionRepository(new[] { session });
            var handler = new ProcessHeartbeatCommandHandler(repo);

            var now = DateTime.UtcNow;
            var command = new ProcessHeartbeatCommand(connId, now);

            var result = await handler.HandleAsync(command, CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal("Healthy", result.Value.HeartbeatStatus);

            var updatedSession = await repo.GetByConnectionIdAsync(connId);
            Assert.NotNull(updatedSession);
            Assert.Equal(now, updatedSession.LastHeartbeatAt);
            Assert.Contains(updatedSession.DomainEvents, e => e is HeartbeatReceivedEvent);
        }

        [Fact]
        public async Task LivenessMonitoringWorker_PerformLivenessCheck_Transitions_Stale_And_Timeout_Sessions()
        {
            var now = DateTime.UtcNow;

            // Session 1: Healthy (recent heartbeat)
            var sessionHealthy = CommunicationSession.Create("CONN-HEALTHY");
            sessionHealthy.Authenticate("PC-01");
            sessionHealthy.Activate();
            sessionHealthy.RecordHeartbeat(now.AddSeconds(-10));

            // Session 2: Stale (missed heartbeat past 30s + 15s grace = 45s threshold)
            var sessionStale = CommunicationSession.Create("CONN-STALE");
            sessionStale.Authenticate("PC-02");
            sessionStale.Activate();
            sessionStale.RecordHeartbeat(now.AddSeconds(-60));

            // Session 3: TimedOut (missed heartbeat past 90s timeout)
            var sessionTimeout = CommunicationSession.Create("CONN-TIMEOUT");
            sessionTimeout.Authenticate("PC-03");
            sessionTimeout.Activate();
            sessionTimeout.RecordHeartbeat(now.AddSeconds(-100));

            var repo = new FakeCommunicationSessionRepository(new[] { sessionHealthy, sessionStale, sessionTimeout });
            var connRegistry = new TcpConnectionRegistry();
            var tcpSessionManager = new TcpSessionManager(connRegistry, new FakeRedisService(), NullLogger<TcpSessionManager>.Instance);

            var serverOptions = Options.Create(new ServerOptions
            {
                HeartbeatInterval = 30,
                HeartbeatGracePeriod = 15,
                HeartbeatTimeout = 90,
                LivenessCheckInterval = 15
            });

            var serviceCollection = new ServiceCollection();
            serviceCollection.AddSingleton<ICommunicationSessionRepository>(repo);
            serviceCollection.AddSingleton<ITcpConnectionRegistry>(connRegistry);
            serviceCollection.AddSingleton<ITcpSessionManager>(tcpSessionManager);
            serviceCollection.AddSingleton<IRedisService>(new FakeRedisService());
            serviceCollection.AddSingleton<ISequenceValidator>(new FakeSequenceValidator());

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            var dbContext = new ApplicationDbContext(options);

            var ws1 = new Workstation { PcId = "PC-01", Name = "PC-01", SiteId = "SITE-1", Hostname = "HOST-01", MacAddress = "AA:BB:CC:DD:EE:01", IpAddress = "127.0.0.1", Status = "ONLINE" };
            var ws2 = new Workstation { PcId = "PC-02", Name = "PC-02", SiteId = "SITE-1", Hostname = "HOST-02", MacAddress = "AA:BB:CC:DD:EE:02", IpAddress = "127.0.0.1", Status = "ONLINE" };
            var ws3 = new Workstation { PcId = "PC-03", Name = "PC-03", SiteId = "SITE-1", Hostname = "HOST-03", MacAddress = "AA:BB:CC:DD:EE:03", IpAddress = "127.0.0.1", Status = "ONLINE" };

            dbContext.Workstations.AddRange(ws1, ws2, ws3);
            await dbContext.SaveChangesAsync();

            serviceCollection.AddSingleton(dbContext);

            var serviceProvider = serviceCollection.BuildServiceProvider();
            var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

            var worker = new LivenessMonitoringWorker(scopeFactory, serverOptions, NullLogger<LivenessMonitoringWorker>.Instance);

            await worker.PerformLivenessCheckAsync(CancellationToken.None);

            // Assertions
            Assert.Equal(ConnectionLifecycleState.Active, sessionHealthy.State);

            Assert.Equal(ConnectionLifecycleState.Degraded, sessionStale.State);
            Assert.Equal(HeartbeatStatus.Degraded, sessionStale.HeartbeatStatus);

            Assert.Equal(ConnectionLifecycleState.Disconnected, sessionTimeout.State);

            var dbWs2 = await dbContext.Workstations.FirstOrDefaultAsync(w => w.PcId == "PC-02");
            Assert.NotNull(dbWs2);
            Assert.Equal("STALE", dbWs2.Status, ignoreCase: true);

            var dbWs3 = await dbContext.Workstations.FirstOrDefaultAsync(w => w.PcId == "PC-03");
            Assert.NotNull(dbWs3);
            Assert.Equal("OFFLINE", dbWs3.Status, ignoreCase: true);
        }

        [Fact]
        public async Task MultiConnection_RaceCondition_Safety_NewConnection_Not_Marked_Offline_By_OldCleanup()
        {
            var now = DateTime.UtcNow;

            // Old timed out connection A
            var sessionOld = CommunicationSession.Create("CONN-OLD-A");
            sessionOld.Authenticate("PC-RACECONDITION");
            sessionOld.Activate();
            sessionOld.RecordHeartbeat(now.AddSeconds(-120));

            var repo = new FakeCommunicationSessionRepository(new[] { sessionOld });

            // Connection registry has new active connection B for same PC-ID!
            var connRegistry = new TcpConnectionRegistry();
            var newConnectionB = new FakeTcpConnection("CONN-NEW-B", "PC-RACECONDITION", ConnectionLifecycleState.Active);
            connRegistry.Register(newConnectionB);

            var tcpSessionManager = new TcpSessionManager(connRegistry, new FakeRedisService(), NullLogger<TcpSessionManager>.Instance);

            var serverOptions = Options.Create(new ServerOptions
            {
                HeartbeatInterval = 30,
                HeartbeatGracePeriod = 15,
                HeartbeatTimeout = 90,
                LivenessCheckInterval = 15
            });

            var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            var dbContext = new ApplicationDbContext(dbOptions);

            var ws = new Workstation { PcId = "PC-RACECONDITION", Name = "PC-RC", SiteId = "SITE-1", Hostname = "HOST-RC", MacAddress = "AA:BB:CC:DD:EE:99", IpAddress = "127.0.0.1", Status = "ONLINE" };
            dbContext.Workstations.Add(ws);
            await dbContext.SaveChangesAsync();

            var serviceCollection = new ServiceCollection();
            serviceCollection.AddSingleton<ICommunicationSessionRepository>(repo);
            serviceCollection.AddSingleton<ITcpConnectionRegistry>(connRegistry);
            serviceCollection.AddSingleton<ITcpSessionManager>(tcpSessionManager);
            serviceCollection.AddSingleton<IRedisService>(new FakeRedisService());
            serviceCollection.AddSingleton<ISequenceValidator>(new FakeSequenceValidator());
            serviceCollection.AddSingleton(dbContext);

            var serviceProvider = serviceCollection.BuildServiceProvider();
            var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

            var worker = new LivenessMonitoringWorker(scopeFactory, serverOptions, NullLogger<LivenessMonitoringWorker>.Instance);

            await worker.PerformLivenessCheckAsync(CancellationToken.None);

            // Verify old session disconnected
            Assert.Equal(ConnectionLifecycleState.Disconnected, sessionOld.State);

            // CRITICAL: Workstation MUST NOT be marked Offline because Connection B is active!
            var dbWs = await dbContext.Workstations.FirstOrDefaultAsync(w => w.PcId == "PC-RACECONDITION");
            Assert.NotNull(dbWs);
            Assert.Equal("ONLINE", dbWs.Status, ignoreCase: true);
        }

        [Fact]
        public async Task Redis_Service_Failure_Resilience_In_Liveness_Check()
        {
            var now = DateTime.UtcNow;

            var sessionTimeout = CommunicationSession.Create("CONN-REDIS-FAIL");
            sessionTimeout.Authenticate("PC-REDIS");
            sessionTimeout.Activate();
            sessionTimeout.RecordHeartbeat(now.AddSeconds(-100));

            var repo = new FakeCommunicationSessionRepository(new[] { sessionTimeout });
            var connRegistry = new TcpConnectionRegistry();
            var tcpSessionManager = new TcpSessionManager(connRegistry, new FailingRedisService(), NullLogger<TcpSessionManager>.Instance);

            var serverOptions = Options.Create(new ServerOptions
            {
                HeartbeatInterval = 30,
                HeartbeatGracePeriod = 15,
                HeartbeatTimeout = 90,
                LivenessCheckInterval = 15
            });

            var serviceCollection = new ServiceCollection();
            serviceCollection.AddSingleton<ICommunicationSessionRepository>(repo);
            serviceCollection.AddSingleton<ITcpConnectionRegistry>(connRegistry);
            serviceCollection.AddSingleton<ITcpSessionManager>(tcpSessionManager);
            serviceCollection.AddSingleton<IRedisService>(new FailingRedisService());
            serviceCollection.AddSingleton<ISequenceValidator>(new FakeSequenceValidator());

            var serviceProvider = serviceCollection.BuildServiceProvider();
            var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

            var worker = new LivenessMonitoringWorker(scopeFactory, serverOptions, NullLogger<LivenessMonitoringWorker>.Instance);

            // Should complete without throwing exception despite Redis failure
            await worker.PerformLivenessCheckAsync(CancellationToken.None);

            Assert.Equal(ConnectionLifecycleState.Disconnected, sessionTimeout.State);
        }

        #region Fake Test Helpers

        private class FakeCommunicationSessionRepository : ICommunicationSessionRepository
        {
            private readonly List<CommunicationSession> _sessions;

            public FakeCommunicationSessionRepository(IEnumerable<CommunicationSession> sessions)
            {
                _sessions = sessions.ToList();
            }

            public Task AddAsync(CommunicationSession session, CancellationToken cancellationToken = default)
            {
                _sessions.Add(session);
                return Task.CompletedTask;
            }

            public Task<CommunicationSession?> GetByConnectionIdAsync(string connectionId, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(_sessions.FirstOrDefault(s => s.ConnectionId == connectionId));
            }

            public Task<CommunicationSession?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(_sessions.FirstOrDefault(s => s.Id == id));
            }

            public Task<CommunicationSession?> GetByPcIdAsync(string pcId, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(_sessions.FirstOrDefault(s => string.Equals(s.PcId, pcId, StringComparison.OrdinalIgnoreCase)));
            }

            public Task<CommunicationSession?> GetByWorkstationIdAsync(Guid workstationId, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(_sessions.FirstOrDefault(s => s.WorkstationId == workstationId));
            }

            public Task<IReadOnlyList<CommunicationSession>> GetActiveSessionsAsync(CancellationToken cancellationToken = default)
            {
                IReadOnlyList<CommunicationSession> list = _sessions.Where(s => s.IsActive).ToList();
                return Task.FromResult(list);
            }

            public Task UpdateAsync(CommunicationSession session, CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }
        }

        private class FakeRedisService : IRedisService
        {
            public Task<string?> GetStringAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
            public Task SetStringAsync(string key, string value, TimeSpan? expiry = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class => Task.FromResult<T?>(null);
            public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;
            public Task<long> IncrementAsync(string key, TimeSpan? expiry = null, CancellationToken cancellationToken = default) => Task.FromResult(1L);
            public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(true);
            public Task<bool> PingAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        }

        private class FailingRedisService : IRedisService
        {
            public Task<string?> GetStringAsync(string key, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Redis Connection Lost");
            public Task SetStringAsync(string key, string value, TimeSpan? expiry = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Redis Connection Lost");
            public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class => throw new InvalidOperationException("Redis Connection Lost");
            public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default) where T : class => throw new InvalidOperationException("Redis Connection Lost");
            public Task<long> IncrementAsync(string key, TimeSpan? expiry = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Redis Connection Lost");
            public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Redis Connection Lost");
            public Task<bool> PingAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        }

        private class FakeSequenceValidator : ISequenceValidator
        {
            public long GetNextOutboundSequence(string sessionId) => 1;
            public bool ValidateInboundSequence(string sessionId, long sequenceNumber, string? messageId = null) => true;
            public void ResetSession(string sessionId) { }
        }

        private class FakeTcpConnection : ITcpConnection
        {
            public string ConnectionId { get; }
            public ConnectionLifecycleState State { get; private set; }
            public byte[]? SessionKey { get; set; } = new byte[32];
            public string? PcId { get; set; }
            public string? SiteId { get; set; }
            public string? Hostname { get; set; }
            public string? ClientVersion { get; set; }
            public DateTime ConnectedAt { get; } = DateTime.UtcNow;
            public DateTime LastActivity { get; set; } = DateTime.UtcNow;
            public string? RemoteIpAddress { get; } = "127.0.0.1";

            public FakeTcpConnection(string connectionId, string pcId, ConnectionLifecycleState state)
            {
                ConnectionId = connectionId;
                PcId = pcId;
                State = state;
            }

            public Stream GetStream() => Stream.Null;
            public Task SendAsync(byte[] data, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task SendFrameAsync(string framePayload, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            public void UpdateState(ConnectionLifecycleState newState) { State = newState; }
            public void Dispose() { }
        }

        #endregion
    }
}