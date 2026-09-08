using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sayra.Backend.Application.Abstractions.Caching;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Abstractions.Transport;
using Sayra.Backend.Application.Telemetry;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Domain.Enums;
using Sayra.Backend.Domain.Telemetry;
using Sayra.Backend.Domain.ValueObjects;
using Sayra.Backend.Infrastructure.Telemetry;
using Xunit;

namespace Sayra.Backend.UnitTests.Telemetry
{
    public class FakeRedisService : IRedisService
    {
        private readonly ConcurrentDictionary<string, object> _store = new(StringComparer.OrdinalIgnoreCase);

        public Task<string?> GetStringAsync(string key, CancellationToken cancellationToken = default)
        {
            if (_store.TryGetValue(key, out var val) && val is string str) return Task.FromResult<string?>(str);
            return Task.FromResult<string?>(null);
        }

        public Task SetStringAsync(string key, string value, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
        {
            _store[key] = value;
            return Task.CompletedTask;
        }

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
        {
            if (_store.TryGetValue(key, out var val) && val is T typedVal) return Task.FromResult<T?>(typedVal);
            return Task.FromResult<T?>(null);
        }

        public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default) where T : class
        {
            _store[key] = value;
            return Task.CompletedTask;
        }

        public Task<long> IncrementAsync(string key, TimeSpan? expiry = null, CancellationToken cancellationToken = default) => Task.FromResult(1L);
        public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(_store.TryRemove(key, out _));
        public Task<bool> PingAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    public class Phase08HardeningAndLoadTests
    {
        [Theory]
        [InlineData(100)]
        [InlineData(500)]
        [InlineData(1000)]
        [InlineData(5000)]
        public async Task FleetLoadSimulation_IngestionThroughput_ExecutesWithinBoundedTimeAndMemory(int clientCount)
        {
            // Arrange
            var redisService = new FakeRedisService();
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
                string pcId = $"PC-LOAD-{i:D5}";
                var wsId = Guid.NewGuid();
                contexts.Add(new TelemetryConnectionContext($"CONN-{i}", pcId, wsId));
                models.Add(new TelemetryModel
                {
                    Cpu = (i % 100),
                    Ram = 4096.0 + i,
                    Uptime = 3600 + i,
                    Timestamp = now,
                    RunningGameName = (i % 2 == 0) ? "Valorant" : "League of Legends",
                    RunningGameCpu = 15.0,
                    RunningGameRam = 2048.0,
                    TotalLaunches = 5,
                    TotalCrashes = 0,
                    TotalRestarts = 0
                });
            }

            long gcBefore = GC.GetTotalMemory(true);
            var sw = Stopwatch.StartNew();

            // Act - Parallel execution simulating concurrent fleet ingestion
            var results = new ConcurrentBag<TelemetryIngestionResult>();
            await Task.WhenAll(Enumerable.Range(0, clientCount).Select(i => Task.Run(async () =>
            {
                var result = await ingestionService.IngestTelemetrySnapshotAsync(contexts[i], models[i], CancellationToken.None);
                results.Add(result);
            })));

            sw.Stop();
            long gcAfter = GC.GetTotalMemory(false);
            long memoryDeltaMb = Math.Max(0, (gcAfter - gcBefore) / (1024 * 1024));

            // Assert
            Assert.Equal(clientCount, results.Count);
            Assert.All(results, r => Assert.True(r.IsAccepted));

            double throughputMsgPerSec = clientCount / (sw.ElapsedMilliseconds / 1000.0 + 0.001);

            // Bounded limits verification
            Assert.True(sw.ElapsedMilliseconds < 15000, $"Load simulation for {clientCount} clients took {sw.ElapsedMilliseconds}ms, exceeding 15000ms threshold.");
            Assert.True(memoryDeltaMb < 250, $"Memory allocation delta was {memoryDeltaMb}MB, exceeding 250MB threshold.");

            // Verify State Store recorded all workstations correctly
            var trackedStates = await stateStore.GetWorkstationStatesAsync(null, null, CancellationToken.None);
            Assert.Equal(clientCount, trackedStates.Count);
        }

        [Fact]
        public async Task ReconnectStorm_Simultaneous500ClientsReconnect_SurvivesWithoutStateCorruptionOrThreadStarvation()
        {
            // Arrange
            int clientCount = 500;
            var redisService = new FakeRedisService();
            var connectionRegistryMock = new Mock<ITcpConnectionRegistry>();

            connectionRegistryMock.Setup(c => c.GetAll()).Returns(new List<ITcpConnection>());

            var stateStore = new WorkstationStateStore(
                redisService,
                connectionRegistryMock.Object,
                Options.Create(new WorkstationStateOptions()),
                NullLogger<WorkstationStateStore>.Instance);

            var now = DateTime.UtcNow;
            var orgId = Guid.NewGuid();
            var siteId = Guid.NewGuid();

            // Act - Concurrent state initialization & heartbeat updates
            var tasks = new List<Task>();
            for (int i = 0; i < clientCount; i++)
            {
                int index = i;
                tasks.Add(Task.Run(async () =>
                {
                    string pcId = $"PC-RECONNECT-{index:D4}";
                    var wsId = Guid.NewGuid();
                    var identity = new WorkstationIdentity(pcId, wsId, siteId, orgId);
                    var heartbeat = new HeartbeatSignal(identity, now, now, now);

                    await stateStore.UpdateFromHeartbeatAsync(identity, heartbeat, $"CONN-{index}-1", CancellationToken.None);
                    // Reconnect simulation (new connection ID for same PC)
                    await stateStore.UpdateFromHeartbeatAsync(identity, heartbeat, $"CONN-{index}-2", CancellationToken.None);
                }));
            }

            var sw = Stopwatch.StartNew();
            await Task.WhenAll(tasks);
            sw.Stop();

            // Assert
            Assert.True(sw.ElapsedMilliseconds < 5000, $"Reconnect storm took {sw.ElapsedMilliseconds}ms, expected < 5000ms.");
            var states = await stateStore.GetWorkstationStatesAsync(null, null, CancellationToken.None);
            Assert.Equal(clientCount, states.Count);
            Assert.All(states, s => Assert.EndsWith("-2", s.ConnectionId));
        }

        [Fact]
        public async Task AlertStorm_1000WorkstationsUnhealthy_SuppressesAlertSpamAndDeduplicatesDeterministically()
        {
            // Arrange
            var store = new ConcurrentDictionary<string, Incident>();
            var repositoryMock = new Mock<IIncidentRepository>();

            repositoryMock
                .Setup(r => r.GetActiveIncidentsForWorkstationAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns<string, CancellationToken>((pcId, ct) =>
                {
                    var list = store.Values
                        .Where(i => i.PcId == pcId && i.LifecycleState != IncidentLifecycleState.Resolved)
                        .ToList();
                    return Task.FromResult<IReadOnlyList<Incident>>(list);
                });

            repositoryMock
                .Setup(r => r.SaveIncidentAsync(It.IsAny<Incident>(), It.IsAny<CancellationToken>()))
                .Returns<Incident, CancellationToken>((inc, ct) =>
                {
                    store.AddOrUpdate(inc.Fingerprint, inc, (k, v) => inc);
                    return Task.CompletedTask;
                });

            var metricsMock = new Mock<IAlertMetrics>();
            var dispatcherMock = new Mock<IAlertNotificationDispatcher>();
            var options = Options.Create(new AlertingOptions { IsEnabled = true });

            var engine = new AlertEvaluationEngine(
                repositoryMock.Object,
                options,
                metricsMock.Object,
                dispatcherMock.Object,
                NullLogger<AlertEvaluationEngine>.Instance);

            int count = 1000;
            var orgId = Guid.NewGuid();
            var siteId = Guid.NewGuid();
            var now = DateTime.UtcNow;

            var results = new List<WorkstationHealthEvaluationResult>(count);
            for (int i = 0; i < count; i++)
            {
                string pcId = $"PC-ALERTSTORM-{i:D4}";
                var identity = new WorkstationIdentity(pcId, Guid.NewGuid(), siteId, orgId);
                results.Add(WorkstationHealthEvaluationResult.CreateOffline(identity, "Connection lost in storm", now, "v1.0"));
            }

            // Act - Evaluate 1000 workstations
            var sw = Stopwatch.StartNew();
            foreach (var res in results)
            {
                await engine.EvaluateHealthResultAsync(res, CancellationToken.None);
            }
            sw.Stop();

            // Assert
            Assert.Equal(count, store.Count);
            Assert.True(sw.ElapsedMilliseconds < 5000, $"Alert storm processing took {sw.ElapsedMilliseconds}ms, expected < 5000ms.");
            Assert.All(store.Values, inc => Assert.Equal(IncidentLifecycleState.Firing, inc.LifecycleState));

            // Re-evaluate wave 2: verify observations increment without producing new incidents
            var sw2 = Stopwatch.StartNew();
            foreach (var res in results)
            {
                await engine.EvaluateHealthResultAsync(res, CancellationToken.None);
            }
            sw2.Stop();

            Assert.Equal(count, store.Count); // Count remains 1000
            Assert.All(store.Values, inc => Assert.Equal(2, inc.ObservationCount));
            Assert.True(sw2.ElapsedMilliseconds < 5000);
        }
    }
}
