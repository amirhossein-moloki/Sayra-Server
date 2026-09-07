using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sayra.Backend.Application.Abstractions.Caching;
using Sayra.Backend.Application.Abstractions.Transport;
using Sayra.Backend.Application.Telemetry;
using Sayra.Backend.Domain.Enums;
using Sayra.Backend.Domain.Telemetry;
using Sayra.Backend.Domain.ValueObjects;
using Xunit;

namespace Sayra.Backend.UnitTests.Telemetry
{
    public class WorkstationHealthUnitTests
    {
        private readonly WorkstationHealthPolicyOptions _policyOptions;
        private readonly IOptions<WorkstationHealthPolicyOptions> _policyAccessor;
        private readonly WorkstationHealthEvaluator _evaluator;

        public WorkstationHealthUnitTests()
        {
            _policyOptions = new WorkstationHealthPolicyOptions
            {
                CpuWarningThresholdPercent = 80.0,
                CpuCriticalThresholdPercent = 95.0,
                CpuRecoveryThresholdPercent = 75.0,
                RamWarningThresholdPercent = 85.0,
                RamCriticalThresholdPercent = 95.0,
                RamRecoveryThresholdPercent = 80.0,
                GameCrashWarningThresholdCount = 2,
                GameCrashCriticalThresholdCount = 4,
                TelemetryStaleThresholdSeconds = 120,
                HeartbeatTimeoutSeconds = 90,
                OfflineTimeoutSeconds = 300
            };
            _policyAccessor = Options.Create(_policyOptions);
            _evaluator = new WorkstationHealthEvaluator(_policyAccessor);
        }

        private WorkstationRealTimeState CreateConnectedState(string pcId = "WS-001", double cpu = 25.0, double ram = 40.0)
        {
            var identity = new WorkstationIdentity(pcId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            var now = DateTime.UtcNow;
            return new WorkstationRealTimeState(identity)
            {
                ConnectionId = "CONN-100",
                IsConnected = true,
                ConnectionState = "Active",
                LastSeenAt = now,
                LastTelemetryReceivedAt = now,
                LastHeartbeatReceivedAt = now,
                Cpu = cpu,
                Ram = ram,
                Uptime = 3600,
                TotalLaunches = 10,
                TotalCrashes = 0,
                TotalRestarts = 0
            };
        }

        [Fact]
        public async Task Evaluate_HealthyState_ReturnsHealthyAnd100Score()
        {
            var state = CreateConnectedState();

            var result = await _evaluator.EvaluateWorkstationHealthAsync(state);

            Assert.NotNull(result);
            Assert.Equal(WorkstationHealthState.Healthy, result.HealthState);
            Assert.Equal(100.0, result.HealthScore);
            Assert.Empty(result.Reasons);
        }

        [Fact]
        public async Task Evaluate_NullState_ReturnsUnknown()
        {
            var result = await _evaluator.EvaluateWorkstationHealthAsync(null);

            Assert.NotNull(result);
            Assert.Equal(WorkstationHealthState.Unknown, result.HealthState);
            Assert.Equal(50.0, result.HealthScore);
            Assert.Single(result.Reasons);
            Assert.Equal(WorkstationHealthState.Unknown, result.Reasons[0].Severity);
        }

        [Fact]
        public async Task Evaluate_DisconnectedAndExceededOfflineTimeout_ReturnsOffline()
        {
            var identity = new WorkstationIdentity("WS-OFFLINE", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            var state = new WorkstationRealTimeState(identity)
            {
                IsConnected = false,
                LastSeenAt = DateTime.UtcNow.AddSeconds(-400)
            };

            var result = await _evaluator.EvaluateWorkstationHealthAsync(state);

            Assert.NotNull(result);
            Assert.Equal(WorkstationHealthState.Offline, result.HealthState);
            Assert.Equal(0.0, result.HealthScore);
            Assert.Single(result.Reasons);
            Assert.Equal(WorkstationHealthReasonCode.ConnectionLost, result.Reasons[0].ReasonCode);
        }

        [Fact]
        public async Task Evaluate_ConnectedWithStaleTelemetry_ReturnsStaleTelemetryReason()
        {
            var state = CreateConnectedState();
            state.LastTelemetryReceivedAt = DateTime.UtcNow.AddSeconds(-150); // Stale threshold is 120s

            var result = await _evaluator.EvaluateWorkstationHealthAsync(state);

            Assert.NotNull(result);
            Assert.Equal(WorkstationHealthState.Warning, result.HealthState);
            Assert.Equal(90.0, result.HealthScore);
            Assert.Single(result.Reasons);
            Assert.Equal(WorkstationHealthReasonCode.TelemetryStale, result.Reasons[0].ReasonCode);
        }

        [Fact]
        public async Task Evaluate_CpuWarningAndCritical_TriggersAppropriateSeverities()
        {
            // Warning CPU 85%
            var stateWarn = CreateConnectedState("WS-CPU-1", cpu: 85.0);
            var resultWarn = await _evaluator.EvaluateWorkstationHealthAsync(stateWarn);

            Assert.Equal(WorkstationHealthState.Warning, resultWarn.HealthState);
            Assert.Equal(90.0, resultWarn.HealthScore);
            Assert.Equal(WorkstationHealthReasonCode.CpuSustainedHigh, resultWarn.Reasons[0].ReasonCode);

            // Critical CPU 97%
            var stateCrit = CreateConnectedState("WS-CPU-2", cpu: 97.0);
            var resultCrit = await _evaluator.EvaluateWorkstationHealthAsync(stateCrit);

            Assert.Equal(WorkstationHealthState.Critical, resultCrit.HealthState);
            Assert.Equal(60.0, resultCrit.HealthScore);
            Assert.Equal(WorkstationHealthReasonCode.CpuSustainedHigh, resultCrit.Reasons[0].ReasonCode);
            Assert.Equal(WorkstationHealthState.Critical, resultCrit.Reasons[0].Severity);
        }

        [Fact]
        public async Task Evaluate_HysteresisRecovery_RetainsHighCpuUntilRecoveryThresholdReached()
        {
            // Step 1: High CPU 90% -> Warning
            var state1 = CreateConnectedState("WS-HYST", cpu: 90.0);
            var res1 = await _evaluator.EvaluateWorkstationHealthAsync(state1);
            Assert.Equal(WorkstationHealthState.Warning, res1.HealthState);

            // Step 2: CPU drops to 78% (below 80% warning threshold but above 75% recovery threshold)
            var state2 = CreateConnectedState("WS-HYST", cpu: 78.0);
            var res2 = await _evaluator.EvaluateWorkstationHealthAsync(state2, previousResult: res1);
            Assert.Equal(WorkstationHealthState.Warning, res2.HealthState);
            Assert.Single(res2.Reasons);
            Assert.Equal(res1.Reasons[0].FirstDetectedAtUtc, res2.Reasons[0].FirstDetectedAtUtc); // Preserved detection time

            // Step 3: CPU drops to 70% (below 75% recovery threshold) -> Fully recovers to Healthy
            var state3 = CreateConnectedState("WS-HYST", cpu: 70.0);
            var res3 = await _evaluator.EvaluateWorkstationHealthAsync(state3, previousResult: res2);
            Assert.Equal(WorkstationHealthState.Healthy, res3.HealthState);
            Assert.Empty(res3.Reasons);
        }

        [Fact]
        public async Task Evaluate_GameCrashThreshold_TriggersCriticalWhenCountExceeded()
        {
            var state = CreateConnectedState("WS-CRASH");
            state.TotalCrashes = 5; // Threshold is >= 4 for Critical

            var result = await _evaluator.EvaluateWorkstationHealthAsync(state);

            Assert.Equal(WorkstationHealthState.Critical, result.HealthState);
            Assert.Single(result.Reasons);
            Assert.Equal(WorkstationHealthReasonCode.GameCrashFrequencyHigh, result.Reasons[0].ReasonCode);
        }

        [Fact]
        public async Task Evaluate_SoftwareUpdateFailedState_TriggersDegraded()
        {
            var state = CreateConnectedState("WS-UPD");
            state.UpdateState = "UPDATE_FAILED";

            var result = await _evaluator.EvaluateWorkstationHealthAsync(state);

            Assert.Equal(WorkstationHealthState.Degraded, result.HealthState);
            Assert.Equal(75.0, result.HealthScore);
            Assert.Single(result.Reasons);
            Assert.Equal(WorkstationHealthReasonCode.UpdateFailed, result.Reasons[0].ReasonCode);
        }

        [Fact]
        public async Task Evaluate_MultipleSimultaneousReasons_CalculatesCombinedScoreAndHighestSeverity()
        {
            var state = CreateConnectedState("WS-MULTI", cpu: 96.0, ram: 90.0); // Critical CPU (-40) + Warning RAM (-10)
            state.UpdateState = "UPDATE_FAILED"; // Degraded Update (-25)

            var result = await _evaluator.EvaluateWorkstationHealthAsync(state);

            Assert.Equal(WorkstationHealthState.Critical, result.HealthState);
            Assert.Equal(3, result.Reasons.Count);
            Assert.Equal(25.0, result.HealthScore); // 100 - 40 - 10 - 25 = 25
        }

        [Fact]
        public async Task FleetPerformance_5000Workstations_EvaluatesSubSecond()
        {
            int fleetSize = 5000;
            var states = new List<WorkstationRealTimeState>(fleetSize);
            var now = DateTime.UtcNow;

            for (int i = 0; i < fleetSize; i++)
            {
                var pcId = $"PERF-WS-{i:D5}";
                var identity = new WorkstationIdentity(pcId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
                states.Add(new WorkstationRealTimeState(identity)
                {
                    IsConnected = true,
                    LastSeenAt = now,
                    LastTelemetryReceivedAt = now,
                    LastHeartbeatReceivedAt = now,
                    Cpu = (i % 10 == 0) ? 92.0 : 30.0,
                    Ram = (i % 20 == 0) ? 88.0 : 45.0,
                    TotalCrashes = (i % 100 == 0) ? 3 : 0
                });
            }

            var sw = Stopwatch.StartNew();
            int evaluatedCount = 0;
            foreach (var st in states)
            {
                var res = await _evaluator.EvaluateWorkstationHealthAsync(st);
                evaluatedCount++;
            }
            sw.Stop();

            Assert.Equal(fleetSize, evaluatedCount);
            Assert.True(sw.ElapsedMilliseconds < 1000, $"Fleet evaluation of {fleetSize} workstations took {sw.ElapsedMilliseconds}ms, exceeding 1000ms threshold.");
        }
    }
}
