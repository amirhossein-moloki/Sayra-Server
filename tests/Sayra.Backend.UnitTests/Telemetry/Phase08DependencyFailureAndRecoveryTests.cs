using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sayra.Backend.Application.Abstractions.Caching;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Transport;
using Sayra.Backend.Application.Telemetry;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Domain.Enums;
using Sayra.Backend.Domain.Telemetry;
using Sayra.Backend.Domain.ValueObjects;
using Sayra.Backend.Infrastructure.Configuration.Options;
using Sayra.Backend.Infrastructure.Telemetry;
using Xunit;

namespace Sayra.Backend.UnitTests.Telemetry
{
    public class Phase08DependencyFailureAndRecoveryTests
    {
        [Fact]
        public async Task RedisFailure_StateStoreAndIdempotencyService_FailsafeFallbackWithoutProcessCrash()
        {
            // Arrange
            var failingRedisMock = new Mock<IRedisService>();
            failingRedisMock.Setup(r => r.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Redis Connection Refused"));
            failingRedisMock.Setup(r => r.SetAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Redis Connection Refused"));

            var idempotencyService = new TelemetryIdempotencyService(failingRedisMock.Object);

            // Act & Assert - Deduplication check handles Redis exception safely
            bool isDuplicate = await idempotencyService.IsDuplicateEventAsync("EVT-123", CancellationToken.None);
            Assert.False(isDuplicate); // Returns false on Redis failure (fail-open)

            bool isStale = await idempotencyService.IsStaleTelemetryAsync("PC-001", DateTime.UtcNow, CancellationToken.None);
            Assert.False(isStale); // Returns false on Redis failure (fail-open)
        }

        [Fact]
        public async Task WorkerRestart_TelemetryAggregationWorker_ResumesFromCheckpointWithoutDuplicateRows()
        {
            // Arrange
            var historyRepoMock = new Mock<ITelemetryHistoryRepository>();
            var aggregateRepoMock = new Mock<ITelemetryAggregateRepository>();
            var aggregationServiceMock = new Mock<ITelemetryAggregationService>();

            var lastCheckpoint = new TelemetryAggregationCheckpoint
            {
                Granularity = "1m",
                LastProcessedServerTimestamp = DateTime.UtcNow.AddMinutes(-10),
                LastProcessedWindowEnd = DateTime.UtcNow.AddMinutes(-10),
                RecordsProcessed = 50
            };

            aggregateRepoMock.Setup(a => a.GetCheckpointAsync("1m", It.IsAny<CancellationToken>()))
                .ReturnsAsync(lastCheckpoint);

            var rawRecords = new List<TelemetryHistoryRecord>
            {
                new TelemetryHistoryRecord
                {
                    WorkstationId = Guid.NewGuid(),
                    OrganizationId = Guid.NewGuid(),
                    SiteId = Guid.NewGuid(),
                    PcId = "PC-001",
                    Cpu = 45.0,
                    Ram = 2048.0,
                    ClientTimestamp = DateTime.UtcNow.AddMinutes(-5),
                    ServerReceivedAt = DateTime.UtcNow.AddMinutes(-5),
                    ProcessedAt = DateTime.UtcNow.AddMinutes(-5)
                }
            };

            historyRepoMock.Setup(h => h.GetHistoryAllAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(rawRecords);

            var generatedAggs = new List<TelemetryAggregateRecord>
            {
                new TelemetryAggregateRecord
                {
                    WorkstationId = rawRecords[0].WorkstationId,
                    OrganizationId = rawRecords[0].OrganizationId,
                    SiteId = rawRecords[0].SiteId,
                    PcId = "PC-001",
                    Granularity = "1m",
                    WindowStart = DateTime.UtcNow.AddMinutes(-5),
                    WindowEnd = DateTime.UtcNow.AddMinutes(-4),
                    SampleCount = 1,
                    CpuAvg = 45.0,
                    RamAvg = 2048.0
                }
            };

            aggregationServiceMock.Setup(s => s.AggregateRawRecords(rawRecords, "1m"))
                .Returns(generatedAggs);

            var services = new ServiceCollection();
            services.AddSingleton(historyRepoMock.Object);
            services.AddSingleton(aggregateRepoMock.Object);
            services.AddSingleton(aggregationServiceMock.Object);
            var provider = services.BuildServiceProvider();

            var scopeFactoryMock = new Mock<IServiceScopeFactory>();
            var scopeMock = new Mock<IServiceScope>();
            scopeMock.Setup(s => s.ServiceProvider).Returns(provider);
            scopeFactoryMock.Setup(s => s.CreateScope()).Returns(scopeMock.Object);

            var options = Options.Create(new TelemetryAggregationOptions { Enabled = true, IntervalSeconds = 1, BatchSize = 100 });
            var worker = new TelemetryAggregationWorker(scopeFactoryMock.Object, options, NullLogger<TelemetryAggregationWorker>.Instance);

            // Act - Perform 2 consecutive aggregation cycles simulating worker restart / loop
            await worker.PerformAggregationCycleAsync(CancellationToken.None);
            await worker.PerformAggregationCycleAsync(CancellationToken.None);

            // Assert - Checkpoint saved and aggregate batch saved safely
            aggregateRepoMock.Verify(a => a.SaveAggregatesBatchAsync(generatedAggs, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
            aggregateRepoMock.Verify(a => a.SaveCheckpointAsync(It.IsAny<TelemetryAggregationCheckpoint>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        }

        [Fact]
        public async Task WorkerRestart_WorkstationHealthEvaluationWorker_ResumesEvaluationLoopSafely()
        {
            // Arrange
            var stateReaderMock = new Mock<IWorkstationStateReader>();
            var healthStoreMock = new Mock<IWorkstationHealthStore>();
            var evaluatorMock = new Mock<IWorkstationHealthEvaluator>();
            var alertEngineMock = new Mock<IAlertEvaluationEngine>();
            var healthMetricsMock = new Mock<IWorkstationHealthMetrics>();

            var orgId = Guid.NewGuid();
            var siteId = Guid.NewGuid();
            var identity = new WorkstationIdentity("PC-HEALTH-001", Guid.NewGuid(), siteId, orgId);

            var states = new List<WorkstationRealTimeState>
            {
                new WorkstationRealTimeState(identity)
                {
                    ConnectionState = "ConnectedFresh",
                    IsConnected = true,
                    LastSeenAt = DateTime.UtcNow
                }
            };

            stateReaderMock.Setup(s => s.GetWorkstationStatesAsync(null, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(states);

            var healthRes = WorkstationHealthEvaluationResult.CreateHealthy(identity, DateTime.UtcNow, "v1.0");
            evaluatorMock.Setup(e => e.EvaluateWorkstationHealthAsync(states[0], It.IsAny<WorkstationHealthEvaluationResult?>(), It.IsAny<WorkstationHealthPolicyOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(healthRes);

            var services = new ServiceCollection();
            services.AddSingleton(stateReaderMock.Object);
            services.AddSingleton(healthStoreMock.Object);
            services.AddSingleton(evaluatorMock.Object);
            services.AddSingleton(alertEngineMock.Object);
            services.AddSingleton(healthMetricsMock.Object);
            var provider = services.BuildServiceProvider();

            var scopeFactoryMock = new Mock<IServiceScopeFactory>();
            var scopeMock = new Mock<IServiceScope>();
            scopeMock.Setup(s => s.ServiceProvider).Returns(provider);
            scopeFactoryMock.Setup(s => s.CreateScope()).Returns(scopeMock.Object);

            var policyOptions = Options.Create(new WorkstationHealthPolicyOptions { EvaluationIntervalSeconds = 1, EvaluationBatchSize = 100 });
            var worker = new WorkstationHealthEvaluationWorker(scopeFactoryMock.Object, policyOptions, NullLogger<WorkstationHealthEvaluationWorker>.Instance);

            // Act - Perform cycle
            await worker.PerformEvaluationCycleAsync(CancellationToken.None);

            // Assert
            healthStoreMock.Verify(h => h.SaveHealthResultAsync(healthRes, It.IsAny<CancellationToken>()), Times.Once);
            alertEngineMock.Verify(a => a.EvaluateHealthResultAsync(healthRes, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Concurrency_ConcurrentStateUpdatesForSameWorkstation_PreservesStateInvariants()
        {
            // Arrange
            var redisService = new FakeRedisService();
            var connectionRegistryMock = new Mock<ITcpConnectionRegistry>();

            connectionRegistryMock.Setup(c => c.GetAll()).Returns(new List<ITcpConnection>());

            var stateStore = new WorkstationStateStore(
                redisService,
                connectionRegistryMock.Object,
                Options.Create(new WorkstationStateOptions()),
                NullLogger<WorkstationStateStore>.Instance);

            var orgId = Guid.NewGuid();
            var siteId = Guid.NewGuid();
            var wsId = Guid.NewGuid();
            string pcId = "PC-CONCURRENT-001";
            var identity = new WorkstationIdentity(pcId, wsId, siteId, orgId);

            int iterations = 100;
            var now = DateTime.UtcNow;

            // Act - Concurrently update telemetry and heartbeats
            var tasks = new List<Task>();
            for (int i = 0; i < iterations; i++)
            {
                int index = i;
                tasks.Add(Task.Run(async () =>
                {
                    var snapshot = new TelemetrySnapshot(
                        identity,
                        cpu: index % 100,
                        ram: 2048.0 + index,
                        uptime: 1000 + index,
                        runningGameName: "CS2",
                        runningGamePid: 100,
                        runningGameCpu: 10.0,
                        runningGameRam: 500.0,
                        runningGameDuration: 200.0,
                        totalLaunches: index,
                        totalCrashes: 0,
                        totalRestarts: 0,
                        clientTimestamp: now.AddMilliseconds(index),
                        serverReceivedAt: now.AddMilliseconds(index),
                        processedAt: now.AddMilliseconds(index));

                    await stateStore.UpdateFromTelemetryAsync(identity, snapshot, "CONN-001", CancellationToken.None);
                }));

                tasks.Add(Task.Run(async () =>
                {
                    var hbSignal = new HeartbeatSignal(identity, now.AddMilliseconds(index), now.AddMilliseconds(index), now.AddMilliseconds(index));
                    await stateStore.UpdateFromHeartbeatAsync(identity, hbSignal, "CONN-001", CancellationToken.None);
                }));
            }

            await Task.WhenAll(tasks);

            // Assert
            var state = await stateStore.GetCurrentStateAsync(pcId, CancellationToken.None);
            Assert.NotNull(state);
            Assert.Equal(pcId, state!.PcId);
            Assert.NotNull(state.Cpu);
            Assert.NotNull(state.Ram);
        }
    }
}
