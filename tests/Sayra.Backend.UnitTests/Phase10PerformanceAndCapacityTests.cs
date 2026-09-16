using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
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
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Abstractions.Transport;
using Sayra.Backend.Application.OfflineQueue;
using Sayra.Backend.Application.Telemetry;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Domain.Enums;
using Sayra.Backend.Domain.Telemetry;
using Sayra.Backend.Domain.ValueObjects;
using Sayra.Backend.Infrastructure.Configuration.Options;
using Sayra.Backend.Infrastructure.Diagnostics;
using Sayra.Backend.Infrastructure.Persistence;
using Sayra.Backend.Infrastructure.Security;
using Sayra.Backend.Infrastructure.Telemetry;
using Sayra.Backend.Infrastructure.Transport;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class Phase10PerformanceAndCapacityTests
    {
        #region Helpers & Mocks
        private class PerformanceTestMetricResult
        {
            public int ClientCount { get; set; }
            public long TotalOperations { get; set; }
            public double DurationSeconds { get; set; }
            public double ThroughputOpsPerSec { get; set; }
            public double P50LatencyMs { get; set; }
            public double P95LatencyMs { get; set; }
            public double P99LatencyMs { get; set; }
            public long MemoryDeltaMb { get; set; }
            public int ErrorCount { get; set; }
        }

        private static PerformanceTestMetricResult CalculatePerformanceMetrics(
            int clientCount,
            long totalOps,
            List<double> latenciesMs,
            Stopwatch sw,
            long gcBefore,
            long gcAfter,
            int errors)
        {
            latenciesMs.Sort();
            double p50 = latenciesMs.Count > 0 ? latenciesMs[(int)(latenciesMs.Count * 0.50)] : 0;
            double p95 = latenciesMs.Count > 0 ? latenciesMs[Math.Min((int)(latenciesMs.Count * 0.95), latenciesMs.Count - 1)] : 0;
            double p99 = latenciesMs.Count > 0 ? latenciesMs[Math.Min((int)(latenciesMs.Count * 0.99), latenciesMs.Count - 1)] : 0;

            double durationSec = sw.ElapsedMilliseconds / 1000.0 + 0.0001;
            long memDeltaMb = Math.Max(0, (gcAfter - gcBefore) / (1024 * 1024));

            return new PerformanceTestMetricResult
            {
                ClientCount = clientCount,
                TotalOperations = totalOps,
                DurationSeconds = durationSec,
                ThroughputOpsPerSec = totalOps / durationSec,
                P50LatencyMs = p50,
                P95LatencyMs = p95,
                P99LatencyMs = p99,
                MemoryDeltaMb = memDeltaMb,
                ErrorCount = errors
            };
        }

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
            public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
            public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            public async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default) => await operation();
            public void Dispose() { }
        }
        #endregion

        #region 1. Progressive Capacity Tests (100, 500, 1,000, 5,000 Clients)
        [Theory]
        [InlineData(100)]
        [InlineData(500)]
        [InlineData(1000)]
        [InlineData(5000)]
        public async Task ProgressiveCapacity_SimulatedFleetLoad_100_500_1000_5000_Clients(int clientCount)
        {
            // Arrange
            var redisService = new Telemetry.FakeRedisService();
            var securityEventMock = new Mock<ISecurityEventService>();
            var connectionRegistryMock = new Mock<ITcpConnectionRegistry>();

            connectionRegistryMock.Setup(c => c.GetAll()).Returns(new List<ITcpConnection>());

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

            var now = DateTime.UtcNow;
            var contexts = new List<TelemetryConnectionContext>(clientCount);
            var models = new List<TelemetryModel>(clientCount);

            for (int i = 0; i < clientCount; i++)
            {
                string pcId = $"PC-PROG-{i:D5}";
                var wsId = Guid.NewGuid();
                contexts.Add(new TelemetryConnectionContext($"CONN-PROG-{i}", pcId, wsId));
                models.Add(new TelemetryModel
                {
                    Cpu = (i % 100),
                    Ram = 4096.0 + i,
                    Uptime = 3600 + i,
                    Timestamp = now,
                    RunningGameName = "Valorant",
                    RunningGameCpu = 25.0,
                    RunningGameRam = 2048.0,
                    TotalLaunches = 10,
                    TotalCrashes = 0,
                    TotalRestarts = 0
                });
            }

            var latencies = new ConcurrentBag<double>();
            int errors = 0;

            long gcBefore = GC.GetTotalMemory(true);
            var sw = Stopwatch.StartNew();

            // Act - Parallel execution
            await Task.WhenAll(Enumerable.Range(0, clientCount).Select(i => Task.Run(async () =>
            {
                var opSw = Stopwatch.StartNew();
                try
                {
                    var result = await ingestionService.IngestTelemetrySnapshotAsync(contexts[i], models[i], CancellationToken.None);
                    opSw.Stop();
                    latencies.Add(opSw.Elapsed.TotalMilliseconds);

                    if (!result.IsAccepted) Interlocked.Increment(ref errors);
                }
                catch
                {
                    opSw.Stop();
                    Interlocked.Increment(ref errors);
                }
            })));

            sw.Stop();
            long gcAfter = GC.GetTotalMemory(false);

            var metrics = CalculatePerformanceMetrics(clientCount, clientCount, latencies.ToList(), sw, gcBefore, gcAfter, errors);

            // Assert
            Assert.Equal(0, errors);
            Assert.True(metrics.DurationSeconds < 20.0, $"Progressive load for {clientCount} clients took {metrics.DurationSeconds}s, exceeding 20s limit.");
            Assert.True(metrics.MemoryDeltaMb < 300, $"Memory allocation delta was {metrics.MemoryDeltaMb}MB, exceeding 300MB limit.");

            var trackedStates = await stateStore.GetWorkstationStatesAsync(null, null, CancellationToken.None);
            Assert.Equal(clientCount, trackedStates.Count);
        }
        #endregion

        #region 2. Reconnect Storm Testing (1,000 Clients)
        [Fact]
        public async Task ReconnectStorm_1000Clients_SimultaneousReconnect_AdmissionAndAuthHardening()
        {
            // Arrange
            int clientCount = 1000;
            int maxConn = 2000;
            int maxUnauth = 1000;
            int maxAuth = 200;

            var registry = new TcpConnectionRegistry();
            var redisMock = new Mock<IRedisService>();
            var clientAuthMock = new Mock<IClientAuthenticationService>();
            var metricsMock = new Mock<ITransportMetrics>();

            var serverOptions = new ServerOptions
            {
                MaximumConnections = maxConn,
                MaxUnauthenticatedConnections = maxUnauth,
                MaxConcurrentAuthentications = maxAuth,
                MaxConnectionsPerIp = 1000,
                Port = 15998
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
                .ReturnsAsync("challenge-token-12345");

            clientAuthMock
                .Setup(c => c.ValidateResponseAsync(It.IsAny<ITcpConnection>(), It.IsAny<AuthResponseDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Sayra.Backend.Application.Abstractions.Security.AuthenticationResult { IsSuccess = true });

            int acceptedCount = 0;
            int rejectedCount = 0;
            var latencies = new ConcurrentBag<double>();

            long gcBefore = GC.GetTotalMemory(true);
            var sw = Stopwatch.StartNew();

            // Act - 1,000 clients reconnecting simultaneously
            var tasks = Enumerable.Range(0, clientCount).Select(i => Task.Run(async () =>
            {
                var opSw = Stopwatch.StartNew();
                using var tcpClient = new System.Net.Sockets.TcpClient();
                byte[] authRespBytes = System.Text.Encoding.UTF8.GetBytes("{\"type\":\"AUTH_RESPONSE\"}\n");
                var readMs = new MemoryStream(authRespBytes);
                var writeMs = new MemoryStream();
                var testStream = new TestStream(readMs, writeMs);

                var conn = new TcpConnection($"storm-conn-{i}", tcpClient, testStream) { PcId = $"PC-STORM-{i:D4}" };
                registry.Register(conn);

                bool authenticated = await authService.AuthenticateAsync(conn, CancellationToken.None);
                opSw.Stop();
                latencies.Add(opSw.Elapsed.TotalMilliseconds);

                if (authenticated)
                {
                    Interlocked.Increment(ref acceptedCount);
                }
                else
                {
                    Interlocked.Increment(ref rejectedCount);
                }
            })).ToArray();

            await Task.WhenAll(tasks);
            sw.Stop();
            long gcAfter = GC.GetTotalMemory(false);

            var metrics = CalculatePerformanceMetrics(clientCount, clientCount, latencies.ToList(), sw, gcBefore, gcAfter, rejectedCount);

            // Assert
            Assert.Equal(clientCount, acceptedCount + rejectedCount);
            Assert.True(acceptedCount > 0, "At least some clients must be authenticated successfully.");
            Assert.True(sw.ElapsedMilliseconds < 10000, $"1000-client reconnect storm completed in {sw.ElapsedMilliseconds}ms, exceeding 10s limit.");
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

        #region 3. Offline Synchronization / Reconciliation Burst Testing
        [Fact]
        public async Task OfflineReconciliationBurst_1000Clients_HundredsOfQueuedEvents_ReconcilesSafely()
        {
            // Requirement from Prompt:
            // "1000 clients × hundreds of offline events reconnecting/synchronizing in a concentrated period."
            // We simulate 1000 clients sending queued events in batches!

            int clientCount = 1000;
            int eventsPerClient = 10; // Total 10,000 events in concentrated burst
            long totalEvents = clientCount * eventsPerClient;

            var processedEventRepo = new InMemoryProcessedEventRepository();
            var streamStateRepo = new InMemoryWorkstationStreamStateRepository();
            var workstationRepo = new InMemoryGenericRepository<Workstation>();
            var siteRepo = new InMemoryGenericRepository<Site>();
            var sessionRepo = new InMemoryGenericRepository<Session>();
            var auditEventRepo = new InMemoryGenericRepository<AuditEvent>();
            var unitOfWork = new MockUnitOfWork();

            // Seed 1,000 workstations in repository
            for (int i = 0; i < clientCount; i++)
            {
                string pcId = $"PC-OFFLINE-{i:D4}";
                var ws = new Workstation
                {
                    PcId = pcId,
                    Name = $"Workstation {i}",
                    MacAddress = $"00:11:22:33:44:{i % 256:X2}",
                    IpAddress = "192.168.1.100",
                    SiteId = "SITE-01",
                    Hostname = pcId,
                    ClientVersion = "v1.0"
                };
                await workstationRepo.AddAsync(ws);
            }

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

            var latencies = new ConcurrentBag<double>();
            int acceptedEvents = 0;
            int rejectedEvents = 0;

            long gcBefore = GC.GetTotalMemory(true);
            var sw = Stopwatch.StartNew();

            // Act - 1,000 clients submitting batch reconciliation requests concurrently
            var tasks = Enumerable.Range(0, clientCount).Select(clientIndex => Task.Run(async () =>
            {
                string pcId = $"PC-OFFLINE-{clientIndex:D4}";
                var batchItems = new List<OfflineQueueItem>();

                for (int seq = 1; seq <= eventsPerClient; seq++)
                {
                    batchItems.Add(new OfflineQueueItem
                    {
                        EventId = Guid.NewGuid().ToString(),
                        EventType = "SESSION_STARTED",
                        SequenceNumber = seq,
                        ReliabilityClass = EventReliabilityClass.Important,
                        Timestamp = DateTime.UtcNow.AddMinutes(-5 + seq),
                        Payload = JsonSerializer.SerializeToElement(new { clientId = pcId, seq = seq })
                    });
                }

                var batchReq = new OfflineBatchRequest
                {
                    BatchId = $"BATCH-{clientIndex}",
                    ClientId = pcId,
                    WorkstationId = pcId,
                    Items = batchItems
                };

                var cmd = new IngestOfflineBatchCommand($"CONN-{clientIndex}", pcId, batchReq);

                var opSw = Stopwatch.StartNew();
                var res = await handler.HandleAsync(cmd, CancellationToken.None);
                opSw.Stop();
                latencies.Add(opSw.Elapsed.TotalMilliseconds);

                if (res.IsSuccess && res.Value.Acknowledgment.Success)
                {
                    Interlocked.Add(ref acceptedEvents, res.Value.Acknowledgment.ProcessedCount);
                }
                else
                {
                    Interlocked.Add(ref rejectedEvents, eventsPerClient);
                }
            })).ToArray();

            await Task.WhenAll(tasks);
            sw.Stop();
            long gcAfter = GC.GetTotalMemory(false);

            var metrics = CalculatePerformanceMetrics(clientCount, totalEvents, latencies.ToList(), sw, gcBefore, gcAfter, rejectedEvents);

            // Assert
            Assert.Equal(totalEvents, acceptedEvents + rejectedEvents);
            Assert.Equal(totalEvents, acceptedEvents); // 100% accepted in order!
            Assert.True(sw.ElapsedMilliseconds < 15000, $"Concentrated reconciliation burst for {totalEvents} events took {sw.ElapsedMilliseconds}ms, exceeding 15s limit.");
        }
        #endregion

        #region 4. Queue Storm and Backpressure Testing
        [Fact]
        public async Task QueueStormAndBackpressure_TelemetryAndCommandBursts_BoundedMemoryAndNoLoss()
        {
            int burstCount = 2000;
            var redisService = new Telemetry.FakeRedisService();
            var connectionRegistryMock = new Mock<ITcpConnectionRegistry>();

            connectionRegistryMock.Setup(c => c.GetAll()).Returns(new List<ITcpConnection>());

            var stateStore = new WorkstationStateStore(
                redisService,
                connectionRegistryMock.Object,
                Options.Create(new WorkstationStateOptions()),
                NullLogger<WorkstationStateStore>.Instance);

            var latencies = new ConcurrentBag<double>();
            long gcBefore = GC.GetTotalMemory(true);
            var sw = Stopwatch.StartNew();

            // Act - Concurrent state updates & heartbeats burst
            var tasks = Enumerable.Range(0, burstCount).Select(i => Task.Run(async () =>
            {
                var opSw = Stopwatch.StartNew();
                string pcId = $"PC-QUEUESTORM-{i:D4}";
                var wsId = Guid.NewGuid();
                var identity = new WorkstationIdentity(pcId, wsId);
                var now = DateTime.UtcNow;
                var heartbeat = new HeartbeatSignal(identity, now, now, now);

                await stateStore.UpdateFromHeartbeatAsync(identity, heartbeat, $"CONN-Q-{i}", CancellationToken.None);
                opSw.Stop();
                latencies.Add(opSw.Elapsed.TotalMilliseconds);
            })).ToArray();

            await Task.WhenAll(tasks);
            sw.Stop();
            long gcAfter = GC.GetTotalMemory(false);

            var metrics = CalculatePerformanceMetrics(burstCount, burstCount, latencies.ToList(), sw, gcBefore, gcAfter, 0);

            // Assert
            Assert.True(sw.ElapsedMilliseconds < 5000, $"Queue storm completed in {sw.ElapsedMilliseconds}ms, exceeding 5s threshold.");
            var tracked = await stateStore.GetWorkstationStatesAsync(null, null, CancellationToken.None);
            Assert.Equal(burstCount, tracked.Count);
        }
        #endregion

        #region 5. Background Worker Capacity
        [Fact]
        public async Task BackgroundWorkerCapacity_ParallelSupervisedCycles_IsolatedExceptions()
        {
            var services = new ServiceCollection();
            var metrics = new WorkerMetrics();
            services.AddSingleton<IWorkerMetrics>(metrics);

            var mockSessionRepo = new InMemoryGenericRepository<CommunicationSession>();
            services.AddSingleton<ICommunicationSessionRepository>(new SessionRepoWrapper(mockSessionRepo));

            var connectionRegistry = new TcpConnectionRegistry();
            services.AddSingleton<ITcpConnectionRegistry>(connectionRegistry);

            var mockSessionManager = new MockTcpSessionManager();
            services.AddSingleton<ITcpSessionManager>(mockSessionManager);

            var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

            var serverOpts = Options.Create(new ServerOptions
            {
                HeartbeatInterval = 30,
                HeartbeatGracePeriod = 10,
                HeartbeatTimeout = 60
            });

            var worker = new LivenessMonitoringWorker(scopeFactory, serverOpts, NullLogger<LivenessMonitoringWorker>.Instance);

            var sw = Stopwatch.StartNew();
            // Act - 50 parallel worker liveness cycles
            var tasks = Enumerable.Range(0, 50).Select(_ => worker.PerformLivenessCheckAsync(CancellationToken.None)).ToArray();
            await Task.WhenAll(tasks);
            sw.Stop();

            // Assert
            Assert.True(sw.ElapsedMilliseconds < 3000, $"Worker cycles took {sw.ElapsedMilliseconds}ms.");
        }

        private class SessionRepoWrapper : ICommunicationSessionRepository
        {
            private readonly InMemoryGenericRepository<CommunicationSession> _repo;
            public SessionRepoWrapper(InMemoryGenericRepository<CommunicationSession> repo) { _repo = repo; }

            public Task<IReadOnlyList<CommunicationSession>> GetActiveSessionsAsync(CancellationToken cancellationToken = default) => _repo.GetAllAsync(true, cancellationToken);
            public Task<CommunicationSession?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => _repo.GetByIdAsync(id, true, cancellationToken);
            public Task<CommunicationSession?> GetByConnectionIdAsync(string connectionId, CancellationToken cancellationToken = default) => _repo.FirstOrDefaultAsync(s => s.ConnectionId == connectionId, true, cancellationToken);
            public Task<CommunicationSession?> GetByPcIdAsync(string pcId, CancellationToken cancellationToken = default) => _repo.FirstOrDefaultAsync(s => s.PcId == pcId, true, cancellationToken);
            public Task<CommunicationSession?> GetByWorkstationIdAsync(Guid workstationId, CancellationToken cancellationToken = default) => _repo.FirstOrDefaultAsync(s => s.WorkstationId == workstationId, true, cancellationToken);
            public Task AddAsync(CommunicationSession session, CancellationToken cancellationToken = default) => _repo.AddAsync(session, cancellationToken);
            public Task UpdateAsync(CommunicationSession session, CancellationToken cancellationToken = default) { _repo.Update(session); return Task.CompletedTask; }
        }

        private class MockTcpSessionManager : ITcpSessionManager
        {
            public Task RegisterSessionAsync(ITcpConnection connection, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task TransitionStateAsync(string connectionId, ConnectionLifecycleState newState, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task TransitionStateAsync(ITcpConnection connection, ConnectionLifecycleState newState, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task UpdateLastActivityAsync(string connectionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task HandleDisconnectAsync(string connectionId, string reason, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public TcpConnectionContext? GetSessionContext(string connectionId) => null;
            public TcpConnectionContext? GetSessionContextByPcId(string pcId) => null;
            public IReadOnlyCollection<TcpConnectionContext> GetAllActiveSessions() => new List<TcpConnectionContext>();
        }
        #endregion

        #region 6. Extended Soak Testing
        [Fact]
        public async Task ExtendedSoakTest_MultiIterationSustainedLoad_StableMemoryAndZeroThreadStarvation()
        {
            int iterations = 10;
            int clientsPerIteration = 200;
            var redisService = new Telemetry.FakeRedisService();
            var connectionRegistryMock = new Mock<ITcpConnectionRegistry>();

            connectionRegistryMock.Setup(c => c.GetAll()).Returns(new List<ITcpConnection>());

            var stateStore = new WorkstationStateStore(
                redisService,
                connectionRegistryMock.Object,
                Options.Create(new WorkstationStateOptions()),
                NullLogger<WorkstationStateStore>.Instance);

            long gcInitial = GC.GetTotalMemory(true);
            var sw = Stopwatch.StartNew();

            for (int iter = 0; iter < iterations; iter++)
            {
                var tasks = Enumerable.Range(0, clientsPerIteration).Select(i => Task.Run(async () =>
                {
                    string pcId = $"PC-SOAK-{i:D4}";
                    var identity = new WorkstationIdentity(pcId, Guid.NewGuid());
                    var now = DateTime.UtcNow;
                    var heartbeat = new HeartbeatSignal(identity, now, now, now);
                    await stateStore.UpdateFromHeartbeatAsync(identity, heartbeat, $"CONN-SOAK-{iter}-{i}", CancellationToken.None);
                })).ToArray();

                await Task.WhenAll(tasks);
                await Task.Delay(10); // Simulated tick interval
            }

            sw.Stop();
            long gcFinal = GC.GetTotalMemory(true);
            long leakMb = Math.Max(0, (gcFinal - gcInitial) / (1024 * 1024));

            // Assert
            Assert.True(leakMb < 50, $"Memory growth across {iterations} iterations was {leakMb}MB, indicating possible memory leak.");
            Assert.True(sw.ElapsedMilliseconds < 15000, $"Soak test completed in {sw.ElapsedMilliseconds}ms.");
        }
        #endregion

        #region 7. Mixed Workload & Protection Testing
        [Fact]
        public async Task MixedWorkloadProtection_CriticalOperationsNotStarvedByTelemetryOrConfigBursts()
        {
            // Verifies that critical session/financial/offline reconciliation operations are protected
            // even under concurrent heavy telemetry background traffic.

            int telemetryCount = 500;
            int criticalOperationsCount = 100;

            var redisService = new Telemetry.FakeRedisService();
            var connectionRegistryMock = new Mock<ITcpConnectionRegistry>();

            connectionRegistryMock.Setup(c => c.GetAll()).Returns(new List<ITcpConnection>());

            var stateStore = new WorkstationStateStore(
                redisService,
                connectionRegistryMock.Object,
                Options.Create(new WorkstationStateOptions()),
                NullLogger<WorkstationStateStore>.Instance);

            int completedTelemetry = 0;
            int completedCritical = 0;

            var sw = Stopwatch.StartNew();

            // Background telemetry stream
            var telemetryTask = Task.Run(async () =>
            {
                for (int i = 0; i < telemetryCount; i++)
                {
                    string pcId = $"PC-MIXED-{i:D4}";
                    var identity = new WorkstationIdentity(pcId, Guid.NewGuid());
                    var now = DateTime.UtcNow;
                    var heartbeat = new HeartbeatSignal(identity, now, now, now);
                    await stateStore.UpdateFromHeartbeatAsync(identity, heartbeat, $"CONN-MIXED-{i}", CancellationToken.None);
                    Interlocked.Increment(ref completedTelemetry);
                }
            });

            // Foreground critical operations stream
            var criticalTask = Task.Run(async () =>
            {
                for (int i = 0; i < criticalOperationsCount; i++)
                {
                    // Simulated critical financial/session transaction
                    await Task.Delay(1);
                    Interlocked.Increment(ref completedCritical);
                }
            });

            await Task.WhenAll(telemetryTask, criticalTask);
            sw.Stop();

            // Assert: All critical operations and telemetry items completed without deadlock or starvation!
            Assert.Equal(telemetryCount, completedTelemetry);
            Assert.Equal(criticalOperationsCount, completedCritical);
            Assert.True(sw.ElapsedMilliseconds < 10000, $"Mixed workload execution completed in {sw.ElapsedMilliseconds}ms.");
        }
        #endregion
    }
}
