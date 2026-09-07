using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using Sayra.Backend.Application.Abstractions.Caching;
using Sayra.Backend.Application.Abstractions.Transport;
using Sayra.Backend.Application.Telemetry;
using Sayra.Backend.Domain.Telemetry;
using Sayra.Backend.Domain.ValueObjects;

namespace Sayra.Backend.UnitTests.Telemetry
{
    public class WorkstationStateUnitTests
    {
        private readonly Mock<IRedisService> _redisServiceMock = new();
        private readonly Mock<ITcpConnectionRegistry> _connectionRegistryMock = new();
        private readonly WorkstationStateOptions _options = new()
        {
            TelemetryStaleThresholdSeconds = 120,
            HeartbeatTimeoutSeconds = 90,
            OfflineTimeoutSeconds = 300,
            RedisStateTtlHours = 24
        };
        private readonly WorkstationStateStore _stateStore;

        public WorkstationStateUnitTests()
        {
            var optionsWrapper = Options.Create(_options);
            _stateStore = new WorkstationStateStore(
                _redisServiceMock.Object,
                _connectionRegistryMock.Object,
                optionsWrapper,
                NullLogger<WorkstationStateStore>.Instance);
        }

        #region 1. Telemetry Snapshot Updates & Partial Field Merging

        [Fact]
        public async Task UpdateFromTelemetry_ValidSnapshot_UpdatesRealTimeState()
        {
            // Arrange
            var identity = new WorkstationIdentity("PC-001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            var now = DateTime.UtcNow;
            var snapshot = new TelemetrySnapshot(
                identity,
                cpu: 45.5,
                ram: 8192.0,
                uptime: 3600.0,
                runningGameName: "Cyberpunk 2077",
                runningGamePid: 4321,
                runningGameCpu: 25.0,
                runningGameRam: 4096.0,
                runningGameDuration: 1800.0,
                totalLaunches: 5,
                totalCrashes: 0,
                totalRestarts: 1,
                clientTimestamp: now,
                serverReceivedAt: now,
                processedAt: now);

            WorkstationRealTimeState? savedState = null;
            _redisServiceMock.Setup(r => r.SetAsync(It.IsAny<string>(), It.IsAny<WorkstationRealTimeState>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
                .Callback<string, WorkstationRealTimeState, TimeSpan?, CancellationToken>((k, s, ttl, ct) => savedState = s)
                .Returns(Task.CompletedTask);

            // Act
            await _stateStore.UpdateFromTelemetryAsync(identity, snapshot, "CONN-100", CancellationToken.None);

            // Assert
            Assert.NotNull(savedState);
            Assert.Equal("PC-001", savedState!.PcId);
            Assert.True(savedState.IsConnected);
            Assert.Equal("CONN-100", savedState.ConnectionId);
            Assert.Equal(45.5, savedState.Cpu);
            Assert.Equal(8192.0, savedState.Ram);
            Assert.Equal("Cyberpunk 2077", savedState.RunningGameName);
            Assert.Equal(4321, savedState.RunningGamePid);
            Assert.Equal(5, savedState.TotalLaunches);
            Assert.Equal(now, savedState.LastTelemetryReceivedAt);
            Assert.Equal(now, savedState.LastTelemetryClientTimestamp);
        }

        [Fact]
        public async Task UpdateFromTelemetry_PartialTelemetry_PreservesUnrelatedExistingState()
        {
            // Arrange
            var identity = new WorkstationIdentity("PC-001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            var now = DateTime.UtcNow;

            var existingState = new WorkstationRealTimeState(identity)
            {
                IsConnected = true,
                CurrentSessionId = "SESS-777",
                UpdateState = "UPDATE_READY",
                RunningGameName = "ExistingGame",
                RunningGamePid = 1111,
                Cpu = 10.0,
                Ram = 2048.0
            };

            _redisServiceMock.Setup(r => r.GetAsync<WorkstationRealTimeState>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(existingState);

            var partialSnapshot = new TelemetrySnapshot(
                identity,
                cpu: 85.0,
                ram: 4096.0,
                uptime: 7200.0,
                runningGameName: null, // Omitted game info
                runningGamePid: null,
                runningGameCpu: null,
                runningGameRam: null,
                runningGameDuration: null,
                totalLaunches: 10,
                totalCrashes: 0,
                totalRestarts: 0,
                clientTimestamp: now,
                serverReceivedAt: now,
                processedAt: now);

            WorkstationRealTimeState? savedState = null;
            _redisServiceMock.Setup(r => r.SetAsync(It.IsAny<string>(), It.IsAny<WorkstationRealTimeState>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
                .Callback<string, WorkstationRealTimeState, TimeSpan?, CancellationToken>((k, s, ttl, ct) => savedState = s)
                .Returns(Task.CompletedTask);

            // Act
            await _stateStore.UpdateFromTelemetryAsync(identity, partialSnapshot, "CONN-100", CancellationToken.None);

            // Assert
            Assert.NotNull(savedState);
            Assert.Equal(85.0, savedState!.Cpu);
            Assert.Equal(4096.0, savedState.Ram);
            Assert.Equal("ExistingGame", savedState.RunningGameName); // Unrelated game state preserved!
            Assert.Equal("SESS-777", savedState.CurrentSessionId); // Session association preserved!
            Assert.Equal("UPDATE_READY", savedState.UpdateState); // Update state preserved!
        }

        #endregion

        #region 2. Ordering & Out-of-Order Telemetry Rejection

        [Fact]
        public async Task UpdateFromTelemetry_OlderClientTimestamp_RejectsMetricOverwrite()
        {
            // Arrange
            var identity = new WorkstationIdentity("PC-001");
            var now = DateTime.UtcNow;

            var existingState = new WorkstationRealTimeState(identity)
            {
                Cpu = 90.0,
                Ram = 8192.0,
                LastTelemetryClientTimestamp = now,
                LastTelemetryReceivedAt = now
            };

            _redisServiceMock.Setup(r => r.GetAsync<WorkstationRealTimeState>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(existingState);

            // Older snapshot arriving late
            var olderSnapshot = new TelemetrySnapshot(
                identity,
                cpu: 10.0,
                ram: 1024.0,
                uptime: 100.0,
                runningGameName: null,
                runningGamePid: null,
                runningGameCpu: null,
                runningGameRam: null,
                runningGameDuration: null,
                totalLaunches: 1,
                totalCrashes: 0,
                totalRestarts: 0,
                clientTimestamp: now.AddMinutes(-5), // 5 minutes older
                serverReceivedAt: now.AddSeconds(1),
                processedAt: now.AddSeconds(1));

            WorkstationRealTimeState? savedState = null;
            _redisServiceMock.Setup(r => r.SetAsync(It.IsAny<string>(), It.IsAny<WorkstationRealTimeState>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
                .Callback<string, WorkstationRealTimeState, TimeSpan?, CancellationToken>((k, s, ttl, ct) => savedState = s)
                .Returns(Task.CompletedTask);

            // Act
            await _stateStore.UpdateFromTelemetryAsync(identity, olderSnapshot, "CONN-100", CancellationToken.None);

            // Assert
            Assert.NotNull(savedState);
            Assert.Equal(90.0, savedState!.Cpu); // Metrics preserved from newer sample!
            Assert.Equal(8192.0, savedState.Ram);
            Assert.Equal(now, savedState.LastTelemetryClientTimestamp); // Timestamp preserved!
        }

        #endregion

        #region 3. Heartbeat Liveness Separation

        [Fact]
        public async Task UpdateFromHeartbeat_UpdatesLivenessWithoutFabricatingTelemetryMetrics()
        {
            // Arrange
            var identity = new WorkstationIdentity("PC-001");
            var now = DateTime.UtcNow;
            var heartbeatSignal = new HeartbeatSignal(identity, clientTimestamp: now, serverReceivedAt: now, processedAt: now);

            WorkstationRealTimeState? savedState = null;
            _redisServiceMock.Setup(r => r.SetAsync(It.IsAny<string>(), It.IsAny<WorkstationRealTimeState>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
                .Callback<string, WorkstationRealTimeState, TimeSpan?, CancellationToken>((k, s, ttl, ct) => savedState = s)
                .Returns(Task.CompletedTask);

            // Act
            await _stateStore.UpdateFromHeartbeatAsync(identity, heartbeatSignal, "CONN-100", CancellationToken.None);

            // Assert
            Assert.NotNull(savedState);
            Assert.True(savedState!.IsConnected);
            Assert.Equal(now, savedState.LastHeartbeatReceivedAt);
            Assert.Equal(now, savedState.LastHeartbeatClientTimestamp);
            Assert.Null(savedState.Cpu); // No fake telemetry fabricated!
            Assert.Null(savedState.Ram);
            Assert.Null(savedState.RunningGameName);
        }

        #endregion

        #region 4. Operational Event Mappings

        [Fact]
        public async Task UpdateFromOperationalEvent_GameStarted_UpdatesRunningGame()
        {
            // Arrange
            var identity = new WorkstationIdentity("PC-001");
            var now = DateTime.UtcNow;
            var eventSignal = new OperationalEventSignal(
                eventId: "EVT-001",
                eventType: "GAME_STARTED",
                identity: identity,
                sessionId: "SESS-100",
                correlationId: "CORR-100",
                occurredAt: now,
                payload: "{\"name\":\"Dota 2\"}",
                serverReceivedAt: now,
                processedAt: now);

            WorkstationRealTimeState? savedState = null;
            _redisServiceMock.Setup(r => r.SetAsync(It.IsAny<string>(), It.IsAny<WorkstationRealTimeState>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
                .Callback<string, WorkstationRealTimeState, TimeSpan?, CancellationToken>((k, s, ttl, ct) => savedState = s)
                .Returns(Task.CompletedTask);

            // Act
            await _stateStore.UpdateFromOperationalEventAsync(identity, eventSignal, CancellationToken.None);

            // Assert
            Assert.NotNull(savedState);
            Assert.Equal("Dota 2", savedState!.RunningGameName);
            Assert.Equal("EVT-001", savedState.LastEventId);
            Assert.Equal("GAME_STARTED", savedState.LastEventType);
        }

        [Fact]
        public async Task UpdateFromOperationalEvent_GameExited_ClearsRunningGameInfo()
        {
            // Arrange
            var identity = new WorkstationIdentity("PC-001");
            var now = DateTime.UtcNow;

            var existingState = new WorkstationRealTimeState(identity)
            {
                RunningGameName = "Dota 2",
                RunningGamePid = 1234,
                RunningGameCpu = 15.0,
                RunningGameRam = 2048.0
            };

            _redisServiceMock.Setup(r => r.GetAsync<WorkstationRealTimeState>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(existingState);

            var eventSignal = new OperationalEventSignal(
                eventId: "EVT-002",
                eventType: "GAME_EXITED",
                identity: identity,
                sessionId: "SESS-100",
                correlationId: "CORR-101",
                occurredAt: now,
                payload: "{}",
                serverReceivedAt: now,
                processedAt: now);

            WorkstationRealTimeState? savedState = null;
            _redisServiceMock.Setup(r => r.SetAsync(It.IsAny<string>(), It.IsAny<WorkstationRealTimeState>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
                .Callback<string, WorkstationRealTimeState, TimeSpan?, CancellationToken>((k, s, ttl, ct) => savedState = s)
                .Returns(Task.CompletedTask);

            // Act
            await _stateStore.UpdateFromOperationalEventAsync(identity, eventSignal, CancellationToken.None);

            // Assert
            Assert.NotNull(savedState);
            Assert.Null(savedState!.RunningGameName);
            Assert.Null(savedState.RunningGamePid);
        }

        [Fact]
        public async Task UpdateFromOperationalEvent_SessionStartedAndEnded_UpdatesAndClearsSessionId()
        {
            // Arrange
            var identity = new WorkstationIdentity("PC-001");
            var now = DateTime.UtcNow;

            var startSignal = new OperationalEventSignal("E1", "SESSION_STARTED", identity, "SESS-555", "C1", now, "{}", now, now);
            var endSignal = new OperationalEventSignal("E2", "SESSION_ENDED", identity, "SESS-555", "C2", now, "{}", now, now);

            WorkstationRealTimeState? savedState = null;
            _redisServiceMock.Setup(r => r.SetAsync(It.IsAny<string>(), It.IsAny<WorkstationRealTimeState>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
                .Callback<string, WorkstationRealTimeState, TimeSpan?, CancellationToken>((k, s, ttl, ct) => savedState = s)
                .Returns(Task.CompletedTask);

            _redisServiceMock.Setup(r => r.GetAsync<WorkstationRealTimeState>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => savedState);

            // Act 1: Session Started
            await _stateStore.UpdateFromOperationalEventAsync(identity, startSignal, CancellationToken.None);
            Assert.Equal("SESS-555", savedState!.CurrentSessionId);

            // Act 2: Session Ended
            await _stateStore.UpdateFromOperationalEventAsync(identity, endSignal, CancellationToken.None);
            Assert.Null(savedState.CurrentSessionId);
        }

        #endregion

        #region 5. Operational Status 5-State Evaluation

        [Fact]
        public async Task EvaluateOperationalState_CorrectlyClassifiesAllFiveStates()
        {
            var now = DateTime.UtcNow;
            var staleThreshold = TimeSpan.FromSeconds(120);
            var offlineTimeout = TimeSpan.FromSeconds(300);

            // 1. ConnectedFresh: Connected + Fresh telemetry
            var freshState = new WorkstationRealTimeState { IsConnected = true, LastTelemetryReceivedAt = now.AddSeconds(-30) };
            Assert.Equal(WorkstationOperationalState.ConnectedFresh, freshState.EvaluateOperationalState(now, staleThreshold, offlineTimeout));

            // 2. ConnectedStale: Connected + Stale telemetry (> 120s)
            var staleState = new WorkstationRealTimeState { IsConnected = true, LastTelemetryReceivedAt = now.AddSeconds(-200) };
            Assert.Equal(WorkstationOperationalState.ConnectedStale, staleState.EvaluateOperationalState(now, staleThreshold, offlineTimeout));

            // 3. Disconnected: Disconnected + LastSeen within offline timeout (<= 300s)
            var disconnectedState = new WorkstationRealTimeState { IsConnected = false, LastSeenAt = now.AddSeconds(-150) };
            Assert.Equal(WorkstationOperationalState.Disconnected, disconnectedState.EvaluateOperationalState(now, staleThreshold, offlineTimeout));

            // 4. Offline: Disconnected + LastSeen beyond offline timeout (> 300s)
            var offlineState = new WorkstationRealTimeState { IsConnected = false, LastSeenAt = now.AddSeconds(-400) };
            Assert.Equal(WorkstationOperationalState.Offline, offlineState.EvaluateOperationalState(now, staleThreshold, offlineTimeout));

            // 5. Unknown: Disconnected + No LastSeen record
            var unknownState = new WorkstationRealTimeState { IsConnected = false, LastSeenAt = null };
            Assert.Equal(WorkstationOperationalState.Unknown, unknownState.EvaluateOperationalState(now, staleThreshold, offlineTimeout));
        }

        #endregion

        #region 6. Redis Error Resilience

        [Fact]
        public async Task GetStateAsync_RedisException_DegradesGracefullyWithoutCrashing()
        {
            // Arrange
            _redisServiceMock.Setup(r => r.GetAsync<WorkstationRealTimeState>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Redis unavailable"));

            // Act
            var result = await _stateStore.GetStateAsync("PC-001", CancellationToken.None);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task SaveStateAsync_RedisException_DegradesGracefullyWithoutCrashing()
        {
            // Arrange
            _redisServiceMock.Setup(r => r.SetAsync(It.IsAny<string>(), It.IsAny<WorkstationRealTimeState>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Redis write timeout"));

            var state = new WorkstationRealTimeState { PcId = "PC-001" };

            // Act & Assert (Should not throw exception)
            await _stateStore.SaveStateAsync(state, CancellationToken.None);
        }

        #endregion

        #region 7. Site / Tenant Boundary Filtering & Fleet Summary

        [Fact]
        public async Task GetWorkstationStatesAsync_EnforcesSiteAndOrganizationBoundaryIsolation()
        {
            // Arrange
            var siteA = Guid.NewGuid();
            var siteB = Guid.NewGuid();
            var orgId = Guid.NewGuid();

            var connA = new Mock<ITcpConnection>();
            connA.Setup(c => c.PcId).Returns("PC-SITE-A");
            var connB = new Mock<ITcpConnection>();
            connB.Setup(c => c.PcId).Returns("PC-SITE-B");

            _connectionRegistryMock.Setup(r => r.GetAll()).Returns(new[] { connA.Object, connB.Object });

            var stateA = new WorkstationRealTimeState { PcId = "PC-SITE-A", SiteId = siteA, OrganizationId = orgId };
            var stateB = new WorkstationRealTimeState { PcId = "PC-SITE-B", SiteId = siteB, OrganizationId = orgId };

            _redisServiceMock.Setup(r => r.GetAsync<WorkstationRealTimeState>("v1:workstation:pcid:PC-SITE-A:state", It.IsAny<CancellationToken>()))
                .ReturnsAsync(stateA);
            _redisServiceMock.Setup(r => r.GetAsync<WorkstationRealTimeState>("v1:workstation:pcid:PC-SITE-B:state", It.IsAny<CancellationToken>()))
                .ReturnsAsync(stateB);

            // Act
            var filteredStates = await _stateStore.GetWorkstationStatesAsync(siteId: siteA, organizationId: orgId, cancellationToken: CancellationToken.None);

            // Assert
            Assert.Single(filteredStates);
            Assert.Equal("PC-SITE-A", filteredStates[0].PcId);
        }

        #endregion
    }
}
