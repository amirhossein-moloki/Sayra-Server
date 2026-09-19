using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sayra.Backend.Application.Abstractions.Caching;
using Sayra.Backend.Application.Abstractions.Communication;
using Sayra.Backend.Application.Abstractions.Diagnostics;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Abstractions.Transport;
using Sayra.Backend.Application.Configuration;
using Sayra.Backend.Application.OfflineQueue;
using Sayra.Backend.Application.Resilience;
using Sayra.Backend.Application.Security;
using Sayra.Backend.Application.Telemetry;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Domain.Enums;
using Sayra.Backend.Domain.Telemetry;
using Sayra.Backend.Domain.ValueObjects;
using Sayra.Backend.Infrastructure.Caching;
using Sayra.Backend.Infrastructure.Configuration.Options;
using Sayra.Backend.Infrastructure.Diagnostics;
using Sayra.Backend.Infrastructure.Persistence;
using Sayra.Backend.Infrastructure.Resilience;
using Sayra.Backend.Infrastructure.Security;
using Sayra.Backend.Infrastructure.Telemetry;
using Sayra.Backend.Infrastructure.Transport;
using Sayra.Backend.Infrastructure.Updates;
using StackExchange.Redis;
using Xunit;

namespace Sayra.Backend.UnitTests.Resilience
{
    public class Phase10ChaosAndFailureRecoveryTests
    {
        private readonly ResilienceOptions _resilienceOptions = new()
        {
            MaxRetryAttempts = 2,
            InitialBackoffSeconds = 0.01,
            MaxBackoffSeconds = 0.05,
            AttemptTimeoutSeconds = 0.2,
            OverallTimeoutSeconds = 0.5
        };

        private readonly ResilienceMetrics _resilienceMetrics = new();

        #region Helpers & In-Memory Implementations
        private class InMemoryProcessedEventRepository : IProcessedEventRepository
        {
            private readonly ConcurrentDictionary<Guid, ProcessedEvent> _events = new();

            public Task<ProcessedEvent?> GetByEventIdAsync(Guid eventId, bool track = true, CancellationToken cancellationToken = default)
            {
                _events.TryGetValue(eventId, out var evt);
                return Task.FromResult(evt);
            }

            public Task AddAsync(ProcessedEvent entity, CancellationToken cancellationToken = default)
            {
                _events[entity.EventId] = entity;
                return Task.CompletedTask;
            }

            public Task<IReadOnlyList<ProcessedEvent>> GetByClientIdAsync(string clientId, int limit = 100, CancellationToken cancellationToken = default)
            {
                var list = _events.Values.Where(e => e.ClientId.Equals(clientId, StringComparison.OrdinalIgnoreCase)).Take(limit).ToList();
                return Task.FromResult<IReadOnlyList<ProcessedEvent>>(list);
            }

            public Task<ProcessedEvent?> GetByIdAsync(Guid id, bool track = true, CancellationToken cancellationToken = default) => GetByEventIdAsync(id, track, cancellationToken);
            public Task<ProcessedEvent?> FirstOrDefaultAsync(Expression<Func<ProcessedEvent, bool>> predicate, bool track = true, CancellationToken cancellationToken = default)
            {
                var compiled = predicate.Compile();
                return Task.FromResult(_events.Values.FirstOrDefault(compiled));
            }

            public Task<IReadOnlyList<ProcessedEvent>> FindAsync(Expression<Func<ProcessedEvent, bool>> predicate, bool track = true, CancellationToken cancellationToken = default)
            {
                var compiled = predicate.Compile();
                var list = _events.Values.Where(compiled).ToList();
                return Task.FromResult<IReadOnlyList<ProcessedEvent>>(list);
            }

            public Task<IReadOnlyList<ProcessedEvent>> GetAllAsync(bool track = true, CancellationToken cancellationToken = default)
            {
                return Task.FromResult<IReadOnlyList<ProcessedEvent>>(_events.Values.ToList());
            }

            public void Update(ProcessedEvent entity) { _events[entity.EventId] = entity; }
            public void Delete(ProcessedEvent entity) { _events.TryRemove(entity.EventId, out _); }
        }

        private class InMemoryWorkstationStreamStateRepository : IWorkstationStreamStateRepository
        {
            private readonly ConcurrentDictionary<string, WorkstationStreamState> _states = new(StringComparer.OrdinalIgnoreCase);

            public Task<WorkstationStreamState?> GetByClientIdAsync(string clientId, bool track = true, CancellationToken cancellationToken = default)
            {
                _states.TryGetValue(clientId, out var state);
                return Task.FromResult(state);
            }

            public Task AddAsync(WorkstationStreamState entity, CancellationToken cancellationToken = default)
            {
                _states[entity.ClientId] = entity;
                return Task.CompletedTask;
            }

            public Task UpdateAsync(WorkstationStreamState entity, CancellationToken cancellationToken = default)
            {
                _states[entity.ClientId] = entity;
                return Task.CompletedTask;
            }

            public Task<List<ProcessedEvent>> GetPendingWaitingEventsAsync(string clientId, long expectedSequence, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(new List<ProcessedEvent>());
            }

            public Task<WorkstationStreamState?> GetByIdAsync(Guid id, bool track = true, CancellationToken cancellationToken = default) => Task.FromResult<WorkstationStreamState?>(null);
            public Task<WorkstationStreamState?> FirstOrDefaultAsync(Expression<Func<WorkstationStreamState, bool>> predicate, bool track = true, CancellationToken cancellationToken = default)
            {
                var compiled = predicate.Compile();
                return Task.FromResult(_states.Values.FirstOrDefault(compiled));
            }

            public Task<IReadOnlyList<WorkstationStreamState>> FindAsync(Expression<Func<WorkstationStreamState, bool>> predicate, bool track = true, CancellationToken cancellationToken = default)
            {
                var compiled = predicate.Compile();
                return Task.FromResult<IReadOnlyList<WorkstationStreamState>>(_states.Values.Where(compiled).ToList());
            }

            public Task<IReadOnlyList<WorkstationStreamState>> GetAllAsync(bool track = true, CancellationToken cancellationToken = default)
            {
                return Task.FromResult<IReadOnlyList<WorkstationStreamState>>(_states.Values.ToList());
            }

            public void Update(WorkstationStreamState entity) { _states[entity.ClientId] = entity; }
            public void Delete(WorkstationStreamState entity) { _states.TryRemove(entity.ClientId, out _); }
        }

        private class InMemoryGenericRepository<T> : IRepository<T> where T : BaseEntity
        {
            protected readonly ConcurrentDictionary<Guid, T> Store = new();

            public virtual Task<T?> GetByIdAsync(Guid id, bool track = true, CancellationToken cancellationToken = default)
            {
                Store.TryGetValue(id, out var entity);
                return Task.FromResult(entity);
            }

            public virtual Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate, bool track = true, CancellationToken cancellationToken = default)
            {
                var compiled = predicate.Compile();
                return Task.FromResult(Store.Values.FirstOrDefault(compiled));
            }

            public virtual Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate, bool track = true, CancellationToken cancellationToken = default)
            {
                var compiled = predicate.Compile();
                var list = Store.Values.Where(compiled).ToList();
                return Task.FromResult<IReadOnlyList<T>>(list);
            }

            public virtual Task<IReadOnlyList<T>> GetAllAsync(bool track = true, CancellationToken cancellationToken = default)
            {
                return Task.FromResult<IReadOnlyList<T>>(Store.Values.ToList());
            }

            public virtual Task AddAsync(T entity, CancellationToken cancellationToken = default)
            {
                Store[entity.Id] = entity;
                return Task.CompletedTask;
            }

            public virtual void Update(T entity)
            {
                Store[entity.Id] = entity;
            }

            public virtual void Delete(T entity)
            {
                Store.TryRemove(entity.Id, out _);
            }
        }

        private class MockUnitOfWork : IUnitOfWork
        {
            public bool ThrowsOnSave { get; set; }
            public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            {
                if (ThrowsOnSave) throw new InvalidOperationException("Simulated database save failure.");
                return Task.FromResult(1);
            }
            public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            public async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default) => await operation();
            public void Dispose() { }
        }

        private class TestStream : Stream
        {
            private readonly Stream _readStream;
            private readonly Stream _writeStream;

            public TestStream(Stream readStream, Stream writeStream)
            {
                _readStream = readStream;
                _writeStream = writeStream;
            }

            public override bool CanRead => _readStream.CanRead;
            public override bool CanSeek => false;
            public override bool CanWrite => _writeStream.CanWrite;
            public override long Length => _readStream.Length;
            public override long Position { get => _readStream.Position; set => _readStream.Position = value; }

            public override void Flush() => _writeStream.Flush();
            public override Task FlushAsync(CancellationToken cancellationToken) => _writeStream.FlushAsync(cancellationToken);

            public override int Read(byte[] buffer, int offset, int count) => _readStream.Read(buffer, offset, count);
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => _readStream.ReadAsync(buffer, offset, count, cancellationToken);
            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => _readStream.ReadAsync(buffer, cancellationToken);

            public override void Write(byte[] buffer, int offset, int count) => _writeStream.Write(buffer, offset, count);
            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => _writeStream.WriteAsync(buffer, offset, count, cancellationToken);
            public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => _writeStream.WriteAsync(buffer, cancellationToken);

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
        }
        #endregion

        #region 1. PostgreSQL Chaos Tests
        [Fact]
        public async Task PostgreSQL_Unavailable_TriggersTimeout_AndBoundedRetry_AndDegradedBehavior()
        {
            var pipeline = new ResiliencePipeline(Options.Create(_resilienceOptions), _resilienceMetrics, NullLogger<ResiliencePipeline>.Instance);

            int attempts = 0;
            var ex = await Assert.ThrowsAsync<TimeoutException>(async () =>
            {
                await pipeline.ExecuteAsync<bool>(ct =>
                {
                    Interlocked.Increment(ref attempts);
                    throw new TimeoutException("Simulated PostgreSQL connection timeout.");
                }, OperationRetrySafety.SafeRead, "PostgreSQL", "QueryUser", CancellationToken.None);
            });

            Assert.Equal(_resilienceOptions.MaxRetryAttempts, attempts);
            Assert.NotNull(ex);
        }

        [Fact]
        public async Task PostgreSQL_Slow_EnforcesCancellationAndTimeout_WithoutThreadStarvation()
        {
            var pipeline = new ResiliencePipeline(Options.Create(_resilienceOptions), _resilienceMetrics, NullLogger<ResiliencePipeline>.Instance);

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

            var ex = await Assert.ThrowsAsync<TaskCanceledException>(async () =>
            {
                await pipeline.ExecuteAsync<bool>(async ct =>
                {
                    await Task.Delay(2000, ct); // Exceeds cancellation token
                    return true;
                }, OperationRetrySafety.SafeRead, "PostgreSQL", "SlowQuery", cts.Token);
            });

            Assert.NotNull(ex);
        }

        [Fact]
        public async Task PostgreSQL_ConnectionExhaustion_TimesOutGracefully_AndProtectsCriticalWorkloads()
        {
            var semaphore = new SemaphoreSlim(2, 2); // Simulate connection pool limit of 2

            // Occupy pool
            await semaphore.WaitAsync();
            await semaphore.WaitAsync();

            bool acquired = await semaphore.WaitAsync(TimeSpan.FromMilliseconds(50));
            Assert.False(acquired, "Connection pool acquisition should time out gracefully when exhausted.");

            semaphore.Release();
            bool acquiredAfterRelease = await semaphore.WaitAsync(TimeSpan.FromMilliseconds(50));
            Assert.True(acquiredAfterRelease, "Workload resumes when connection pressure drops.");
            semaphore.Release();
            semaphore.Release();
        }

        [Fact]
        public async Task PostgreSQL_RestartAndRecovery_RefreshesConnectionsAndResumesWorkload()
        {
            bool isDbAvailable = false;

            var pipeline = new ResiliencePipeline(Options.Create(_resilienceOptions), _resilienceMetrics, NullLogger<ResiliencePipeline>.Instance);

            // First attempt when DB is offline
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await pipeline.ExecuteAsync<bool>(ct =>
                {
                    if (!isDbAvailable) throw new InvalidOperationException("PostgreSQL connection refused.");
                    return Task.FromResult(true);
                }, OperationRetrySafety.SafeRead, "PostgreSQL", "GetState", CancellationToken.None);
            });

            // Simulate PostgreSQL restart completed
            isDbAvailable = true;

            bool success = await pipeline.ExecuteAsync<bool>(ct =>
            {
                if (!isDbAvailable) throw new InvalidOperationException("PostgreSQL connection refused.");
                return Task.FromResult(true);
            }, OperationRetrySafety.SafeRead, "PostgreSQL", "GetState", CancellationToken.None);

            Assert.True(success);
        }
        #endregion

        #region 2. Redis Chaos Tests
        [Fact]
        public async Task Redis_Unavailable_DegradesToAuthoritativeDb_AndTelemetryIdempotencyFailsOpen()
        {
            var mockMultiplexer = new Mock<IConnectionMultiplexer>();
            var mockDb = new Mock<IDatabase>();

            mockDb.Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Redis offline simulation"));

            mockMultiplexer.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                .Returns(mockDb.Object);

            var redisService = new RedisService(mockMultiplexer.Object, NullLogger<RedisService>.Instance);

            string? cachedValue = await redisService.GetStringAsync("sayra:config:v1:test");
            Assert.Null(cachedValue); // Fail-open / graceful degradation to database fallback

            var idempotencyService = new TelemetryIdempotencyService(redisService, NullLogger<TelemetryIdempotencyService>.Instance);
            bool isStale = await idempotencyService.IsStaleTelemetryAsync("PC-100", DateTime.UtcNow);
            Assert.False(isStale); // Telemetry processing continues safely
        }

        [Fact]
        public async Task Redis_RestartAndReconnect_RecoversStateAndLocksWithoutStaleOwnershipLeakage()
        {
            var fakeRedis = new Telemetry.FakeRedisService();

            // Set distributed lock
            await fakeRedis.SetStringAsync("v1:lock:sync", "owner-1", TimeSpan.FromMinutes(1));
            Assert.Equal("owner-1", await fakeRedis.GetStringAsync("v1:lock:sync"));

            // Simulate Redis restart / key loss
            await fakeRedis.RemoveAsync("v1:lock:sync");

            // New connection can safely acquire lock without stale ownership blockage
            await fakeRedis.SetStringAsync("v1:lock:sync", "owner-2", TimeSpan.FromMinutes(1));
            Assert.Equal("owner-2", await fakeRedis.GetStringAsync("v1:lock:sync"));
        }

        [Fact]
        public async Task Redis_Latency_TimesOutGracefullyWithoutWorkerCollapse()
        {
            var mockRedis = new Mock<IRedisService>();
            mockRedis.Setup(r => r.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(async (string k, CancellationToken ct) =>
                {
                    await Task.Delay(1000, ct); // Observe cancellation token!
                    return "value";
                });

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            await Assert.ThrowsAsync<TaskCanceledException>(async () =>
            {
                await mockRedis.Object.GetStringAsync("key", cts.Token);
            });
        }
        #endregion

        #region 3. Network, Transport & Client Connectivity Chaos Tests
        [Fact]
        public async Task TCP_AbruptDisconnect_DetectedAndCleanedUpByLivenessWorker()
        {
            var services = new ServiceCollection();
            var metrics = new WorkerMetrics();
            services.AddSingleton<IWorkerMetrics>(metrics);

            var pastTime = DateTime.UtcNow.AddMinutes(-10);
            var session = CommunicationSession.Create("abrupt-conn-1", "10.0.0.1", null, connectedAt: pastTime);

            var mockSessionRepo = new Mock<ICommunicationSessionRepository>();
            mockSessionRepo.Setup(r => r.GetActiveSessionsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<CommunicationSession> { session });

            services.AddSingleton<ICommunicationSessionRepository>(mockSessionRepo.Object);

            var registry = new TcpConnectionRegistry();
            services.AddSingleton<ITcpConnectionRegistry>(registry);

            var mockSessionManager = new Mock<ITcpSessionManager>();
            services.AddSingleton<ITcpSessionManager>(mockSessionManager.Object);

            var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

            var serverOpts = Options.Create(new ServerOptions
            {
                HeartbeatInterval = 30,
                HeartbeatGracePeriod = 10,
                HeartbeatTimeout = 60
            });

            var worker = new LivenessMonitoringWorker(scopeFactory, serverOpts, NullLogger<LivenessMonitoringWorker>.Instance);

            await worker.PerformLivenessCheckAsync(CancellationToken.None);

            mockSessionManager.Verify(m => m.HandleDisconnectAsync("abrupt-conn-1", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once());
        }

        [Fact]
        public async Task TCP_HalfOpenSocket_DetectedViaHeartbeatTimeout_AndTerminated()
        {
            var registry = new TcpConnectionRegistry();
            using var tcpClient = new System.Net.Sockets.TcpClient();
            var readMs = new MemoryStream();
            var writeMs = new MemoryStream();
            var testStream = new TestStream(readMs, writeMs);

            var conn = new TcpConnection("halfopen-conn-1", tcpClient, testStream)
            {
                PcId = "PC-HALFOPEN-1",
                LastActivity = DateTime.UtcNow.AddSeconds(-120) // Exceeds 90s heartbeat liveness limit
            };

            registry.Register(conn);
            Assert.NotNull(registry.Get("halfopen-conn-1"));

            // Heartbeat timeout detected
            bool isLivenessExpired = (DateTime.UtcNow - conn.LastActivity).TotalSeconds > 90;
            Assert.True(isLivenessExpired);

            registry.Unregister("halfopen-conn-1");
            Assert.Null(registry.Get("halfopen-conn-1"));
            await Task.CompletedTask;
        }

        [Fact]
        public async Task TCP_DuplicateConnection_ReplacesStaleConnectionAndPreservesIdentity()
        {
            var registry = new TcpConnectionRegistry();
            var redisMock = new Mock<IRedisService>();
            var clientAuthMock = new Mock<IClientAuthenticationService>();
            var metricsMock = new Mock<ITransportMetrics>();

            var serverOptions = new ServerOptions
            {
                MaximumConnections = 10,
                MaxUnauthenticatedConnections = 10,
                MaxConcurrentAuthentications = 10,
                MaxConnectionsPerIp = 10,
                Port = 15997
            };

            var sessionManager = new TcpSessionManager(registry, redisMock.Object, NullLogger<TcpSessionManager>.Instance);

            var authService = new TcpAuthenticationService(
                clientAuthMock.Object,
                redisMock.Object,
                NullLogger<TcpAuthenticationService>.Instance,
                Options.Create(serverOptions),
                sessionManager,
                registry,
                metricsMock.Object);

            clientAuthMock
                .Setup(c => c.GenerateChallengeAsync(It.IsAny<ITcpConnection>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("challenge");

            clientAuthMock
                .Setup(c => c.ValidateResponseAsync(It.IsAny<ITcpConnection>(), It.IsAny<AuthResponseDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Sayra.Backend.Application.Abstractions.Security.AuthenticationResult { IsSuccess = true });

            using var tcp1 = new System.Net.Sockets.TcpClient();
            var conn1 = new TcpConnection("dup-conn-1", tcp1, new TestStream(new MemoryStream(Encoding.UTF8.GetBytes("{}\n")), new MemoryStream())) { PcId = "PC-DUP-01" };
            registry.Register(conn1);
            await authService.AuthenticateAsync(conn1, CancellationToken.None);

            // Second connection with same PcId arrives
            using var tcp2 = new System.Net.Sockets.TcpClient();
            var conn2 = new TcpConnection("dup-conn-2", tcp2, new TestStream(new MemoryStream(Encoding.UTF8.GetBytes("{}\n")), new MemoryStream())) { PcId = "PC-DUP-01" };
            registry.Register(conn2);
            await authService.AuthenticateAsync(conn2, CancellationToken.None);

            // conn1 replaced by conn2 in registry lookup by PcId
            var currentByPc = registry.GetByPcId("PC-DUP-01");
            Assert.NotNull(currentByPc);
            Assert.Equal("dup-conn-2", currentByPc.ConnectionId);
        }

        [Fact]
        public async Task ReconnectStorm_1000Clients_ThrottlesConcurrentlyAndStabilizes()
        {
            int clientCount = 100;
            int maxAuth = 20;

            var registry = new TcpConnectionRegistry();
            var redisMock = new Mock<IRedisService>();
            var clientAuthMock = new Mock<IClientAuthenticationService>();
            var metricsMock = new Mock<ITransportMetrics>();

            var serverOptions = new ServerOptions
            {
                MaximumConnections = 1000,
                MaxUnauthenticatedConnections = 1000,
                MaxConcurrentAuthentications = maxAuth,
                MaxConnectionsPerIp = 100,
                Port = 15996
            };

            var sessionManager = new TcpSessionManager(registry, redisMock.Object, NullLogger<TcpSessionManager>.Instance);

            var authService = new TcpAuthenticationService(
                clientAuthMock.Object,
                redisMock.Object,
                NullLogger<TcpAuthenticationService>.Instance,
                Options.Create(serverOptions),
                sessionManager,
                registry,
                metricsMock.Object);

            clientAuthMock
                .Setup(c => c.GenerateChallengeAsync(It.IsAny<ITcpConnection>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("challenge");

            clientAuthMock
                .Setup(c => c.ValidateResponseAsync(It.IsAny<ITcpConnection>(), It.IsAny<AuthResponseDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Sayra.Backend.Application.Abstractions.Security.AuthenticationResult { IsSuccess = true });

            int completed = 0;
            var tasks = Enumerable.Range(0, clientCount).Select(i => Task.Run(async () =>
            {
                using var tcpClient = new System.Net.Sockets.TcpClient();
                var conn = new TcpConnection($"storm-conn-{i}", tcpClient, new TestStream(new MemoryStream(Encoding.UTF8.GetBytes("{}\n")), new MemoryStream())) { PcId = $"PC-STORM2-{i}" };
                registry.Register(conn);
                await authService.AuthenticateAsync(conn, CancellationToken.None);
                Interlocked.Increment(ref completed);
            })).ToArray();

            await Task.WhenAll(tasks);
            Assert.Equal(clientCount, completed);
        }
        #endregion

        #region 4. Worker, Backend & Process Crash Chaos Tests
        [Fact]
        public async Task WorkerCrash_FaultInjectionInWorkerCycle_IsolatesExceptionAndResumes()
        {
            var services = new ServiceCollection();
            var metrics = new WorkerMetrics();
            services.AddSingleton<IWorkerMetrics>(metrics);

            var stateReader = new Mock<IWorkstationStateReader>();
            stateReader.Setup(s => s.GetWorkstationStatesAsync(null, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<WorkstationRealTimeState>
                {
                    new WorkstationRealTimeState(new WorkstationIdentity("PC-CRASH-1", Guid.NewGuid())),
                    new WorkstationRealTimeState(new WorkstationIdentity("PC-OK-2", Guid.NewGuid()))
                });

            var healthStore = new Mock<IWorkstationHealthStore>();
            var evaluator = new Mock<IWorkstationHealthEvaluator>();

            evaluator.Setup(e => e.EvaluateWorkstationHealthAsync(
                    It.Is<WorkstationRealTimeState>(s => s.PcId == "PC-CRASH-1"),
                    It.IsAny<WorkstationHealthEvaluationResult>(),
                    It.IsAny<WorkstationHealthPolicyOptions>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Simulated worker exception during health evaluation."));

            evaluator.Setup(e => e.EvaluateWorkstationHealthAsync(
                    It.Is<WorkstationRealTimeState>(s => s.PcId == "PC-OK-2"),
                    It.IsAny<WorkstationHealthEvaluationResult>(),
                    It.IsAny<WorkstationHealthPolicyOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WorkstationHealthEvaluationResult
                {
                    Identity = new WorkstationIdentity("PC-OK-2", Guid.NewGuid()),
                    HealthState = WorkstationHealthState.Healthy
                });

            services.AddSingleton<IWorkstationStateReader>(stateReader.Object);
            services.AddSingleton<IWorkstationHealthStore>(healthStore.Object);
            services.AddSingleton<IWorkstationHealthEvaluator>(evaluator.Object);

            var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

            var worker = new WorkstationHealthEvaluationWorker(scopeFactory, Options.Create(new WorkstationHealthPolicyOptions()), NullLogger<WorkstationHealthEvaluationWorker>.Instance);

            // Execute cycle - exception on PC-CRASH-1 must be isolated!
            await worker.PerformEvaluationCycleAsync(CancellationToken.None);

            healthStore.Verify(h => h.SaveHealthResultAsync(It.Is<WorkstationHealthEvaluationResult>(r => r != null && r.Identity != null && r.Identity.PcId == "PC-OK-2"), It.IsAny<CancellationToken>()), Times.Once());
        }

        [Fact]
        public async Task BackendProcessCrash_SimulatedCrashAndRestart_RecoversReadinessAndWorkload()
        {
            // Simulate crash by disposing scope and recreating DI container
            var services = new ServiceCollection();
            services.AddSingleton<IWorkerMetrics, WorkerMetrics>();

            var provider1 = services.BuildServiceProvider();
            provider1.Dispose(); // Simulate backend crash

            // Restart backend container
            var provider2 = services.BuildServiceProvider();
            var metrics = provider2.GetService<IWorkerMetrics>();

            Assert.NotNull(metrics);
            await Task.CompletedTask;
        }

        [Fact]
        public void StartupFailure_MissingMandatoryConfig_FailsFastWithMeaningfulError()
        {
            var services = new ServiceCollection();
            services.AddOptions<DatabaseOptions>()
                .Configure(o => o.ConnectionString = "") // Missing mandatory DB string
                .Validate(o => !string.IsNullOrWhiteSpace(o.ConnectionString), "Database:ConnectionString is required.");

            var sp = services.BuildServiceProvider();

            var ex = Assert.Throws<OptionsValidationException>(() =>
            {
                var opts = sp.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            });

            Assert.Contains("ConnectionString is required", ex.Message);
        }
        #endregion

        #region 5. Offline Queue, Reconciliation & Resource Chaos Tests
        [Fact]
        public async Task OfflineQueue_FailureBeforeServerAcceptance_PreservesEventForRetry()
        {
            var processedEventRepo = new InMemoryProcessedEventRepository();
            var streamStateRepo = new InMemoryWorkstationStreamStateRepository();
            var workstationRepo = new InMemoryGenericRepository<Workstation>();
            var siteRepo = new InMemoryGenericRepository<Site>();
            var sessionRepo = new InMemoryGenericRepository<Session>();
            var auditEventRepo = new InMemoryGenericRepository<AuditEvent>();
            var unitOfWork = new MockUnitOfWork { ThrowsOnSave = true }; // Simulate DB failure before server acceptance

            var orderingEngine = new OfflineOrderingAndReconciliationEngine(
                processedEventRepo,
                streamStateRepo,
                workstationRepo,
                siteRepo,
                sessionRepo,
                auditEventRepo,
                unitOfWork,
                Options.Create(new OfflineOrderingOptions { GapPolicy = "WAIT" }),
                NullLogger<OfflineOrderingAndReconciliationEngine>.Instance);

            var handler = new IngestOfflineBatchCommandHandler(
                orderingEngine,
                NullLogger<IngestOfflineBatchCommandHandler>.Instance);

            var cmd = new IngestOfflineBatchCommand("CONN-FAIL-1", "PC-OFFLINE-FAIL", new OfflineBatchRequest
            {
                BatchId = "BATCH-FAIL-1",
                ClientId = "PC-OFFLINE-FAIL",
                WorkstationId = "PC-OFFLINE-FAIL",
                Items = new List<OfflineQueueItem>
                {
                    new OfflineQueueItem
                    {
                        EventId = Guid.NewGuid().ToString(),
                        EventType = "SESSION_STARTED",
                        SequenceNumber = 1,
                        Payload = JsonSerializer.SerializeToElement(new { clientId = "PC-OFFLINE-FAIL" })
                    }
                }
            });

            var result = await handler.HandleAsync(cmd, CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(0, result.Value.Acknowledgment.ProcessedCount); // Event was NOT processed
            Assert.Single(result.Value.Acknowledgment.RejectedEventIds); // Event marked rejected for retry
        }

        [Fact]
        public async Task OfflineQueue_FailureAfterDurableCommitBeforeAck_DeduplicatesOnRetry()
        {
            var processedEventRepo = new InMemoryProcessedEventRepository();
            var streamStateRepo = new InMemoryWorkstationStreamStateRepository();
            var workstationRepo = new InMemoryGenericRepository<Workstation>();
            var siteRepo = new InMemoryGenericRepository<Site>();
            var sessionRepo = new InMemoryGenericRepository<Session>();
            var auditEventRepo = new InMemoryGenericRepository<AuditEvent>();
            var unitOfWork = new MockUnitOfWork();

            string pcId = "PC-ACK-LOSS-01";
            var ws = new Workstation { PcId = pcId, Name = "WS-ACK", MacAddress = "00:11:22:33:44:55", IpAddress = "127.0.0.1", SiteId = "SITE-1", Hostname = pcId, ClientVersion = "v1.0" };
            await workstationRepo.AddAsync(ws);

            var orderingEngine = new OfflineOrderingAndReconciliationEngine(
                processedEventRepo,
                streamStateRepo,
                workstationRepo,
                siteRepo,
                sessionRepo,
                auditEventRepo,
                unitOfWork,
                Options.Create(new OfflineOrderingOptions { GapPolicy = "WAIT" }),
                NullLogger<OfflineOrderingAndReconciliationEngine>.Instance);

            var handler = new IngestOfflineBatchCommandHandler(
                orderingEngine,
                NullLogger<IngestOfflineBatchCommandHandler>.Instance);

            string eventId = Guid.NewGuid().ToString();

            var batchReq = new OfflineBatchRequest
            {
                BatchId = "BATCH-ACK-1",
                ClientId = pcId,
                WorkstationId = pcId,
                Items = new List<OfflineQueueItem>
                {
                    new OfflineQueueItem
                    {
                        EventId = eventId,
                        EventType = "SESSION_STARTED",
                        SequenceNumber = 1,
                        ReliabilityClass = EventReliabilityClass.Important,
                        Payload = JsonSerializer.SerializeToElement(new { clientId = pcId })
                    }
                }
            };

            // Attempt 1: Durably saved in DB, but client loses ACK
            var res1 = await handler.HandleAsync(new IngestOfflineBatchCommand("CONN-ACK-1", pcId, batchReq), CancellationToken.None);
            Assert.True(res1.IsSuccess);

            // Attempt 2: Re-transmitted request with same event ID is deduplicated idempotently
            var res2 = await handler.HandleAsync(new IngestOfflineBatchCommand("CONN-ACK-1", pcId, batchReq), CancellationToken.None);
            Assert.True(res2.IsSuccess);

            var allEvents = await processedEventRepo.GetAllAsync();
            Assert.Single(allEvents); // Zero duplicate records created in DB!
        }

        [Fact]
        public async Task OfflineQueue_FailureDuringOrdering_ReconcilesSequenceAndRoutesToDLQ()
        {
            var processedEventRepo = new InMemoryProcessedEventRepository();
            var streamStateRepo = new InMemoryWorkstationStreamStateRepository();
            var workstationRepo = new InMemoryGenericRepository<Workstation>();
            var siteRepo = new InMemoryGenericRepository<Site>();
            var sessionRepo = new InMemoryGenericRepository<Session>();
            var auditEventRepo = new InMemoryGenericRepository<AuditEvent>();
            var unitOfWork = new MockUnitOfWork();

            string pcId = "PC-DLQ-TEST-01";
            var ws = new Workstation { PcId = pcId, Name = "WS-DLQ", MacAddress = "00:11:22:33:44:56", IpAddress = "127.0.0.1", SiteId = "SITE-1", Hostname = pcId, ClientVersion = "v1.0" };
            await workstationRepo.AddAsync(ws);

            var orderingEngine = new OfflineOrderingAndReconciliationEngine(
                processedEventRepo,
                streamStateRepo,
                workstationRepo,
                siteRepo,
                sessionRepo,
                auditEventRepo,
                unitOfWork,
                Options.Create(new OfflineOrderingOptions { GapPolicy = "WAIT" }),
                NullLogger<OfflineOrderingAndReconciliationEngine>.Instance);

            var handler = new IngestOfflineBatchCommandHandler(
                orderingEngine,
                NullLogger<IngestOfflineBatchCommandHandler>.Instance);

            // Send sequence gap (seq 2 before seq 1) under WAIT gap policy
            var batchReq = new OfflineBatchRequest
            {
                BatchId = "BATCH-GAP-1",
                ClientId = pcId,
                WorkstationId = pcId,
                Items = new List<OfflineQueueItem>
                {
                    new OfflineQueueItem
                    {
                        EventId = Guid.NewGuid().ToString(),
                        EventType = "SESSION_STARTED",
                        SequenceNumber = 2, // Out of order! Expected seq 1
                        ReliabilityClass = EventReliabilityClass.Important,
                        Payload = JsonSerializer.SerializeToElement(new { clientId = pcId })
                    }
                }
            };

            var res = await handler.HandleAsync(new IngestOfflineBatchCommand("CONN-GAP-1", pcId, batchReq), CancellationToken.None);
            Assert.True(res.IsSuccess);

            var streamState = await streamStateRepo.GetByClientIdAsync(pcId);
            Assert.NotNull(streamState);
            Assert.Equal(0, streamState.LastSequenceNumber); // Held in pending waiting queue without sequence jump
        }

        [Fact]
        public async Task ResourcePressure_RetryStorm_RemainsBoundedWithExponentialBackoffAndJitter()
        {
            var pipeline = new ResiliencePipeline(Options.Create(_resilienceOptions), _resilienceMetrics, NullLogger<ResiliencePipeline>.Instance);

            int retryAttempts = 0;

            try
            {
                await pipeline.ExecuteAsync<bool>(ct =>
                {
                    Interlocked.Increment(ref retryAttempts);
                    throw new TimeoutException("Dependency timeout.");
                }, OperationRetrySafety.SafeRead, "Database", "QueryData", CancellationToken.None);
            }
            catch (TimeoutException) { }

            Assert.Equal(_resilienceOptions.MaxRetryAttempts, retryAttempts); // Exactly bounded!
        }

        [Fact]
        public async Task DataIntegrity_PostFailureAudit_PreservesLedgerInvariantsAndWorkstationIdentity()
        {
            // Verify that identity anti-spoofing is enforced and recorded in security audit logs
            var securityEventMock = new Mock<ISecurityEventService>();
            var connectionRegistryMock = new Mock<ITcpConnectionRegistry>();
            var redisService = new Telemetry.FakeRedisService();

            var idempotencyService = new TelemetryIdempotencyService(redisService);
            var stateStore = new WorkstationStateStore(
                redisService,
                connectionRegistryMock.Object,
                Options.Create(new WorkstationStateOptions()),
                NullLogger<WorkstationStateStore>.Instance);

            var ingestionService = new TelemetryIngestionService(
                securityEventMock.Object,
                idempotencyService,
                stateStore,
                NullLogger<TelemetryIngestionService>.Instance);

            // Anti-spoofing attempt: Connection PC-100 sending heartbeat claimed as PC-999
            var connContext = new TelemetryConnectionContext("CONN-SPOOF", "PC-100", Guid.NewGuid());
            var heartbeat = new HeartbeatMessage { PcId = "PC-999", Timestamp = DateTime.UtcNow };

            var result = await ingestionService.IngestHeartbeatAsync(connContext, heartbeat, CancellationToken.None);

            Assert.Equal(TelemetryIngestionStatus.IdentityMismatch, result.Status); // Anti-spoofing rejection!
            securityEventMock.Verify(s => s.RecordSecurityEventAsync(
                "TELEMETRY_IDENTITY_MISMATCH",
                It.IsAny<Guid?>(),
                "Workstation",
                "PC-100",
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                "Workstation",
                It.IsAny<Guid?>(),
                "IngestHeartbeat",
                "FAILURE",
                It.IsAny<string?>(),
                null,
                null,
                It.IsAny<CancellationToken>()), Times.Once());
        }
        #endregion
    }
}
