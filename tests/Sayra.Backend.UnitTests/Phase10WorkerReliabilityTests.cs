using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sayra.Backend.Application.Abstractions.Caching;
using Sayra.Backend.Application.Abstractions.Communication;
using Sayra.Backend.Application.Abstractions.Diagnostics;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Abstractions.Transport;
using Sayra.Backend.Application.Telemetry;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Domain.Enums;
using Sayra.Backend.Domain.Telemetry;
using Sayra.Backend.Domain.ValueObjects;
using Sayra.Backend.Infrastructure.Configuration.Options;
using Sayra.Backend.Infrastructure.Diagnostics;
using Sayra.Backend.Infrastructure.Telemetry;
using Sayra.Backend.Infrastructure.Transport;
using Xunit;

namespace Sayra.Backend.UnitTests.Transport
{
    public class Phase10WorkerReliabilityTests
    {
        [Fact]
        public async Task LivenessMonitoringWorker_Should_Isolate_Exceptions_Across_Sessions()
        {
            // Arrange
            var services = new ServiceCollection();
            var metrics = new WorkerMetrics();
            services.AddSingleton<IWorkerMetrics>(metrics);

            var pastTime = DateTime.UtcNow.AddMinutes(-10);
            var session1 = CommunicationSession.Create("conn-1", "127.0.0.1", null, connectedAt: pastTime);
            var session2 = CommunicationSession.Create("conn-2", "127.0.0.2", null, connectedAt: pastTime);

            var mockSessionRepo = new FaultySessionRepository(new List<CommunicationSession> { session1, session2 }, throwOnConnId: "conn-1");
            services.AddSingleton<ICommunicationSessionRepository>(mockSessionRepo);

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

            // Act
            await worker.PerformLivenessCheckAsync(CancellationToken.None);

            // Assert
            // conn-1 throws an exception during processing, but conn-2 must still be processed safely!
            Assert.Contains("conn-2", mockSessionManager.DisconnectedConnectionIds);
        }

        [Fact]
        public async Task RemoteCommandTimeoutWorker_Should_Evaluate_Timeouts_And_Record_Metrics()
        {
            // Arrange
            var services = new ServiceCollection();
            var metrics = new WorkerMetrics();
            services.AddSingleton<IWorkerMetrics>(metrics);

            var mockCommandManager = new MockRemoteCommandManager();
            services.AddSingleton<IRemoteCommandManager>(mockCommandManager);

            var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
            var worker = new RemoteCommandTimeoutWorker(scopeFactory, NullLogger<RemoteCommandTimeoutWorker>.Instance);

            // Act
            await worker.PerformTimeoutEvaluationCycleAsync(CancellationToken.None);

            // Assert
            Assert.True(mockCommandManager.EvaluateTimeoutsCalled);
        }

        [Fact]
        public async Task TelemetryAggregationWorker_Should_Isolate_Rollup_Stage_Errors()
        {
            // Arrange
            var services = new ServiceCollection();
            var metrics = new WorkerMetrics();
            services.AddSingleton<IWorkerMetrics>(metrics);

            var historyRepo = new MockTelemetryHistoryRepository();
            var aggregateRepo = new FaultyTelemetryAggregateRepository(throwOn1m: true);
            var aggService = new MockTelemetryAggregationService();

            services.AddSingleton<ITelemetryHistoryRepository>(historyRepo);
            services.AddSingleton<ITelemetryAggregateRepository>(aggregateRepo);
            services.AddSingleton<ITelemetryAggregationService>(aggService);

            var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
            var options = Options.Create(new TelemetryAggregationOptions
            {
                Enabled = true,
                IntervalSeconds = 10,
                BatchSize = 100
            });

            var worker = new TelemetryAggregationWorker(scopeFactory, options, NullLogger<TelemetryAggregationWorker>.Instance);

            // Act
            await worker.PerformAggregationCycleAsync(CancellationToken.None);

            // Assert: Even though 1m aggregate threw an error, subsequent rollup stages (1m->5m) executed!
            Assert.True(aggregateRepo.GetAggregatesAllCalled);
        }

        [Fact]
        public async Task WorkstationHealthEvaluationWorker_Should_Isolate_Individual_Workstation_Errors()
        {
            // Arrange
            var services = new ServiceCollection();
            var metrics = new WorkerMetrics();
            services.AddSingleton<IWorkerMetrics>(metrics);

            var stateReader = new MockWorkstationStateReader(new List<WorkstationRealTimeState>
            {
                new WorkstationRealTimeState(new WorkstationIdentity("PC-FAULTY", Guid.NewGuid())),
                new WorkstationRealTimeState(new WorkstationIdentity("PC-GOOD", Guid.NewGuid()))
            });

            var healthStore = new MockWorkstationHealthStore();
            var evaluator = new FaultyWorkstationHealthEvaluator(throwOnPcId: "PC-FAULTY");

            services.AddSingleton<IWorkstationStateReader>(stateReader);
            services.AddSingleton<IWorkstationHealthStore>(healthStore);
            services.AddSingleton<IWorkstationHealthEvaluator>(evaluator);

            var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
            var policyOpts = Options.Create(new WorkstationHealthPolicyOptions
            {
                EvaluationIntervalSeconds = 10,
                EvaluationBatchSize = 100
            });

            var worker = new WorkstationHealthEvaluationWorker(scopeFactory, policyOpts, NullLogger<WorkstationHealthEvaluationWorker>.Instance);

            // Act
            await worker.PerformEvaluationCycleAsync(CancellationToken.None);

            // Assert: PC-FAULTY failed, but PC-GOOD was evaluated and saved successfully!
            Assert.True(healthStore.SavedHealthResults.ContainsKey("PC-GOOD"));
            Assert.False(healthStore.SavedHealthResults.ContainsKey("PC-FAULTY"));
        }

        #region Mock Implementations
        private class FaultySessionRepository : ICommunicationSessionRepository
        {
            private readonly List<CommunicationSession> _sessions;
            private readonly string _throwOnConnId;

            public FaultySessionRepository(List<CommunicationSession> sessions, string throwOnConnId)
            {
                _sessions = sessions;
                _throwOnConnId = throwOnConnId;
            }

            public Task<IReadOnlyList<CommunicationSession>> GetActiveSessionsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CommunicationSession>>(_sessions);
            public Task<CommunicationSession?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<CommunicationSession?>(null);
            public Task<CommunicationSession?> GetByConnectionIdAsync(string connectionId, CancellationToken cancellationToken = default) => Task.FromResult<CommunicationSession?>(null);
            public Task<CommunicationSession?> GetByPcIdAsync(string pcId, CancellationToken cancellationToken = default) => Task.FromResult<CommunicationSession?>(null);
            public Task<CommunicationSession?> GetByWorkstationIdAsync(Guid workstationId, CancellationToken cancellationToken = default) => Task.FromResult<CommunicationSession?>(null);
            public Task AddAsync(CommunicationSession session, CancellationToken cancellationToken = default) => Task.CompletedTask;

            public Task UpdateAsync(CommunicationSession session, CancellationToken cancellationToken = default)
            {
                if (session.ConnectionId == _throwOnConnId)
                {
                    throw new InvalidOperationException("Simulated session update failure.");
                }
                return Task.CompletedTask;
            }
        }

        private class MockTcpSessionManager : ITcpSessionManager
        {
            public List<string> DisconnectedConnectionIds { get; } = new();

            public Task RegisterSessionAsync(ITcpConnection connection, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task TransitionStateAsync(string connectionId, ConnectionLifecycleState newState, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task TransitionStateAsync(ITcpConnection connection, ConnectionLifecycleState newState, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task UpdateLastActivityAsync(string connectionId, CancellationToken cancellationToken = default) => Task.CompletedTask;

            public Task HandleDisconnectAsync(string connectionId, string reason, CancellationToken cancellationToken = default)
            {
                DisconnectedConnectionIds.Add(connectionId);
                return Task.CompletedTask;
            }

            public TcpConnectionContext? GetSessionContext(string connectionId) => null;
            public TcpConnectionContext? GetSessionContextByPcId(string pcId) => null;
            public IReadOnlyCollection<TcpConnectionContext> GetAllActiveSessions() => new List<TcpConnectionContext>();
        }

        private class MockRemoteCommandManager : IRemoteCommandManager
        {
            public bool EvaluateTimeoutsCalled { get; private set; }

            public Task<Sayra.Backend.Shared.Result<Sayra.Backend.Contracts.RemoteCommandResponseDto>> CreateAndDispatchCommandAsync(Sayra.Backend.Contracts.CreateRemoteCommandRequestDto request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
            public Task<Sayra.Backend.Shared.Result<bool>> ProcessCommandAckAsync(string commandId, string pcId, string status, string? failureReason, CancellationToken cancellationToken = default) => throw new NotImplementedException();
            public Task<Sayra.Backend.Shared.Result<bool>> ProcessCommandResultAsync(string commandId, string pcId, string status, string? message, string? errorCode, string? resultPayload, CancellationToken cancellationToken = default) => throw new NotImplementedException();
            public Task<Sayra.Backend.Shared.Result<bool>> CancelCommandAsync(string commandId, string requestedBy, string? reason, CancellationToken cancellationToken = default) => throw new NotImplementedException();

            public Task EvaluateTimeoutsAsync(CancellationToken cancellationToken = default)
            {
                EvaluateTimeoutsCalled = true;
                return Task.CompletedTask;
            }
        }

        private class MockTelemetryHistoryRepository : ITelemetryHistoryRepository
        {
            public Task<IReadOnlyList<TelemetryHistoryRecord>> GetHistoryAllAsync(DateTime? fromUtc, DateTime? toUtc, int limit = 1000, CancellationToken cancellationToken = default)
            {
                return Task.FromResult<IReadOnlyList<TelemetryHistoryRecord>>(new List<TelemetryHistoryRecord>());
            }

            public Task AddAsync(TelemetryHistoryRecord record, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task AddHeartbeatAsync(HeartbeatHistoryRecord record, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task AddHeartbeatRecordAsync(HeartbeatHistoryRecord record, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task AddBatchAsync(IEnumerable<TelemetryHistoryRecord> records, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task<IReadOnlyList<TelemetryHistoryRecord>> GetHistoryForWorkstationAsync(Guid workstationId, DateTime? fromUtc, DateTime? toUtc, int limit = 1000, CancellationToken cancellationToken = default) => throw new NotImplementedException();
            public Task<IReadOnlyList<TelemetryHistoryRecord>> GetHistoryForSiteAsync(Guid siteId, DateTime? fromUtc, DateTime? toUtc, int limit = 1000, CancellationToken cancellationToken = default) => throw new NotImplementedException();
            public Task<IReadOnlyList<TelemetryHistoryRecord>> GetHistoryForOrganizationAsync(Guid organizationId, DateTime? fromUtc, DateTime? toUtc, int limit = 1000, CancellationToken cancellationToken = default) => throw new NotImplementedException();
            public Task<IReadOnlyList<HeartbeatHistoryRecord>> GetHeartbeatsForWorkstationAsync(Guid workstationId, DateTime? fromUtc, DateTime? toUtc, int limit = 1000, CancellationToken cancellationToken = default) => throw new NotImplementedException();

            public Task<TelemetryHistoryRecord?> GetByIdAsync(Guid id, bool asNoTracking = true, CancellationToken cancellationToken = default) => throw new NotImplementedException();
            public Task<TelemetryHistoryRecord?> FirstOrDefaultAsync(Expression<Func<TelemetryHistoryRecord, bool>> predicate, bool asNoTracking = true, CancellationToken cancellationToken = default) => throw new NotImplementedException();
            public Task<IReadOnlyList<TelemetryHistoryRecord>> FindAsync(Expression<Func<TelemetryHistoryRecord, bool>> predicate, bool asNoTracking = true, CancellationToken cancellationToken = default) => throw new NotImplementedException();
            public Task<IReadOnlyList<TelemetryHistoryRecord>> GetAllAsync(bool asNoTracking = true, CancellationToken cancellationToken = default) => throw new NotImplementedException();
            public void Update(TelemetryHistoryRecord entity) => throw new NotImplementedException();
            public void Delete(TelemetryHistoryRecord entity) => throw new NotImplementedException();
        }

        private class FaultyTelemetryAggregateRepository : ITelemetryAggregateRepository
        {
            private readonly bool _throwOn1m;
            public bool GetAggregatesAllCalled { get; private set; }

            public FaultyTelemetryAggregateRepository(bool throwOn1m)
            {
                _throwOn1m = throwOn1m;
            }

            public Task<TelemetryAggregationCheckpoint?> GetCheckpointAsync(string granularity, CancellationToken cancellationToken = default)
            {
                return Task.FromResult<TelemetryAggregationCheckpoint?>(null);
            }

            public Task SaveCheckpointAsync(TelemetryAggregationCheckpoint checkpoint, CancellationToken cancellationToken = default) => Task.CompletedTask;

            public Task SaveAggregatesBatchAsync(IEnumerable<TelemetryAggregateRecord> aggregates, CancellationToken cancellationToken = default)
            {
                if (_throwOn1m)
                {
                    throw new InvalidOperationException("Simulated 1m aggregate failure.");
                }
                return Task.CompletedTask;
            }

            public Task<IReadOnlyList<TelemetryAggregateRecord>> GetAggregatesAllAsync(string granularity, DateTime? windowStartFromUtc, DateTime? windowStartToUtc, int limit = 1000, CancellationToken cancellationToken = default)
            {
                GetAggregatesAllCalled = true;
                return Task.FromResult<IReadOnlyList<TelemetryAggregateRecord>>(new List<TelemetryAggregateRecord>());
            }

            public Task<IReadOnlyList<TelemetryAggregateRecord>> GetAggregatesForWorkstationAsync(Guid workstationId, string granularity, DateTime? windowStartFromUtc, DateTime? windowStartToUtc, int limit = 1000, CancellationToken cancellationToken = default) => throw new NotImplementedException();
            public Task<IReadOnlyList<TelemetryAggregateRecord>> GetAggregatesForSiteAsync(Guid siteId, string granularity, DateTime? windowStartFromUtc, DateTime? windowStartToUtc, int limit = 1000, CancellationToken cancellationToken = default) => throw new NotImplementedException();
            public Task<IReadOnlyList<TelemetryAggregateRecord>> GetAggregatesForOrganizationAsync(Guid organizationId, string granularity, DateTime? windowStartFromUtc, DateTime? windowStartToUtc, int limit = 1000, CancellationToken cancellationToken = default) => throw new NotImplementedException();

            public Task<TelemetryAggregateRecord?> GetByIdAsync(Guid id, bool asNoTracking = true, CancellationToken cancellationToken = default) => throw new NotImplementedException();
            public Task<TelemetryAggregateRecord?> FirstOrDefaultAsync(Expression<Func<TelemetryAggregateRecord, bool>> predicate, bool asNoTracking = true, CancellationToken cancellationToken = default) => throw new NotImplementedException();
            public Task<IReadOnlyList<TelemetryAggregateRecord>> FindAsync(Expression<Func<TelemetryAggregateRecord, bool>> predicate, bool asNoTracking = true, CancellationToken cancellationToken = default) => throw new NotImplementedException();
            public Task<IReadOnlyList<TelemetryAggregateRecord>> GetAllAsync(bool asNoTracking = true, CancellationToken cancellationToken = default) => throw new NotImplementedException();
            public Task AddAsync(TelemetryAggregateRecord entity, CancellationToken cancellationToken = default) => throw new NotImplementedException();
            public void Update(TelemetryAggregateRecord entity) => throw new NotImplementedException();
            public void Delete(TelemetryAggregateRecord entity) => throw new NotImplementedException();
        }

        private class MockTelemetryAggregationService : ITelemetryAggregationService
        {
            public IReadOnlyList<TelemetryAggregateRecord> AggregateRawRecords(IEnumerable<TelemetryHistoryRecord> rawRecords, string granularity = "1m")
            {
                return new List<TelemetryAggregateRecord>
                {
                    new TelemetryAggregateRecord
                    {
                        WorkstationId = Guid.NewGuid(),
                        PcId = "PC-01",
                        Granularity = "1m",
                        WindowStart = DateTime.UtcNow.AddMinutes(-1),
                        WindowEnd = DateTime.UtcNow
                    }
                };
            }

            public IReadOnlyList<TelemetryAggregateRecord> RollupAggregates(IEnumerable<TelemetryAggregateRecord> sourceAggregates, string targetGranularity) => new List<TelemetryAggregateRecord>();
            public (DateTime windowStart, DateTime windowEnd) GetWindowBoundaries(DateTime timestamp, string granularity) => (DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow);
        }

        private class MockWorkstationStateReader : IWorkstationStateReader
        {
            private readonly List<WorkstationRealTimeState> _states;

            public MockWorkstationStateReader(List<WorkstationRealTimeState> states)
            {
                _states = states;
            }

            public Task<WorkstationRealTimeState?> GetCurrentStateAsync(string pcId, CancellationToken cancellationToken = default) => Task.FromResult<WorkstationRealTimeState?>(null);
            public Task<WorkstationRealTimeState?> GetCurrentStateByWorkstationIdAsync(Guid workstationId, CancellationToken cancellationToken = default) => Task.FromResult<WorkstationRealTimeState?>(null);

            public Task<IReadOnlyList<WorkstationRealTimeState>> GetWorkstationStatesAsync(Guid? siteId = null, Guid? organizationId = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WorkstationRealTimeState>>(_states);
            public Task<FleetStateSummary> GetFleetSummaryAsync(Guid? siteId = null, Guid? organizationId = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        }

        private class MockWorkstationHealthStore : IWorkstationHealthStore
        {
            public Dictionary<string, WorkstationHealthEvaluationResult> SavedHealthResults { get; } = new();

            public Task<WorkstationHealthEvaluationResult?> GetHealthResultAsync(string pcId, CancellationToken cancellationToken = default) => Task.FromResult<WorkstationHealthEvaluationResult?>(null);
            public Task<WorkstationHealthEvaluationResult?> GetHealthResultByWorkstationIdAsync(Guid workstationId, CancellationToken cancellationToken = default) => Task.FromResult<WorkstationHealthEvaluationResult?>(null);

            public Task SaveHealthResultAsync(WorkstationHealthEvaluationResult healthResult, CancellationToken cancellationToken = default)
            {
                SavedHealthResults[healthResult.Identity.PcId] = healthResult;
                return Task.CompletedTask;
            }
        }

        private class FaultyWorkstationHealthEvaluator : IWorkstationHealthEvaluator
        {
            private readonly string _throwOnPcId;

            public FaultyWorkstationHealthEvaluator(string throwOnPcId)
            {
                _throwOnPcId = throwOnPcId;
            }

            public Task<WorkstationHealthEvaluationResult> EvaluateWorkstationHealthAsync(WorkstationRealTimeState? currentState, WorkstationHealthEvaluationResult? previousHealth = null, WorkstationHealthPolicyOptions? customPolicy = null, CancellationToken cancellationToken = default)
            {
                if (currentState?.PcId == _throwOnPcId)
                {
                    throw new InvalidOperationException("Simulated evaluation error.");
                }

                return Task.FromResult(new WorkstationHealthEvaluationResult
                {
                    Identity = currentState?.GetIdentity() ?? new WorkstationIdentity("PC-UNKNOWN"),
                    HealthState = WorkstationHealthState.Healthy,
                    HealthScore = 100,
                    EvaluatedAtUtc = DateTime.UtcNow
                });
            }
        }
        #endregion
    }
}
