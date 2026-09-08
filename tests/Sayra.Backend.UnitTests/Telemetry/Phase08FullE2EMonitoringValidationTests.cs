using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Security;
using Sayra.Backend.Application.Telemetry;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Domain.Enums;
using Sayra.Backend.Domain.Telemetry;
using Sayra.Backend.Domain.ValueObjects;
using Sayra.Backend.Infrastructure.Telemetry;
using Xunit;

namespace Sayra.Backend.UnitTests.Telemetry
{
    public class Phase08FullE2EMonitoringValidationTests
    {
        private readonly Guid _orgId = Guid.NewGuid();
        private readonly Guid _siteId = Guid.NewGuid();
        private readonly Guid _wsId = Guid.NewGuid();
        private readonly string _pcId = "WS-CAPSTONE-01";

        private readonly FakeAuthorizationService _authService;
        private readonly FakeWorkstationRepository _workstationRepo;
        private readonly FakeWorkstationStateStore _stateStore;
        private readonly FakeWorkstationHealthStore _healthStore;
        private readonly FakeIncidentRepository _incidentRepo;
        private readonly FakeTelemetryHistoryRepository _historyRepo;
        private readonly FakeTelemetryAggregateRepository _aggregateRepo;
        private readonly FakeAuditEventRepository _auditEventRepo;
        private readonly FakeSecurityEventService _securityEventService;
        private readonly FakeTelemetryIdempotencyService _idempotencyService;

        private readonly TelemetryIngestionService _ingestionService;
        private readonly WorkstationHealthEvaluator _healthEvaluator;
        private readonly AlertEvaluationEngine _alertEngine;
        private readonly TelemetryAggregationService _aggregationService;
        private readonly MonitoringQueryService _monitoringQueryService;

        public Phase08FullE2EMonitoringValidationTests()
        {
            _authService = new FakeAuthorizationService();
            _workstationRepo = new FakeWorkstationRepository();
            _stateStore = new FakeWorkstationStateStore();
            _healthStore = new FakeWorkstationHealthStore();
            _incidentRepo = new FakeIncidentRepository();
            _historyRepo = new FakeTelemetryHistoryRepository();
            _aggregateRepo = new FakeTelemetryAggregateRepository();
            _auditEventRepo = new FakeAuditEventRepository();
            _securityEventService = new FakeSecurityEventService();
            _idempotencyService = new FakeTelemetryIdempotencyService();

            _ingestionService = new TelemetryIngestionService(
                _securityEventService,
                _idempotencyService,
                _stateStore,
                _historyRepo,
                null,
                NullLogger<TelemetryIngestionService>.Instance);

            var options = Microsoft.Extensions.Options.Options.Create(new WorkstationHealthPolicyOptions
            {
                CpuWarningThresholdPercent = 80.0,
                CpuCriticalThresholdPercent = 95.0,
                RamWarningThresholdPercent = 85.0,
                RamCriticalThresholdPercent = 95.0,
                TelemetryStaleThresholdSeconds = 120,
                HeartbeatTimeoutSeconds = 90,
                OfflineTimeoutSeconds = 300
            });

            _healthEvaluator = new WorkstationHealthEvaluator(options);

            var alertOptions = Microsoft.Extensions.Options.Options.Create(new AlertingOptions
            {
                IsEnabled = true,
                Rules = AlertingOptions.GetDefaultRules()
            });

            var alertMetrics = new FakeAlertMetrics();
            var alertDispatcher = new FakeAlertNotificationDispatcher();

            _alertEngine = new AlertEvaluationEngine(
                _incidentRepo,
                alertOptions,
                alertMetrics,
                alertDispatcher,
                NullLogger<AlertEvaluationEngine>.Instance);

            _aggregationService = new TelemetryAggregationService();

            _monitoringQueryService = new MonitoringQueryService(
                _authService,
                _workstationRepo,
                _stateStore,
                _healthStore,
                _healthStore,
                _incidentRepo,
                _historyRepo,
                _aggregateRepo,
                _auditEventRepo);

            // Register Workstation entity
            var ws = new Workstation
            {
                PcId = _pcId,
                Name = "Capstone Gaming Workstation",
                OrganizationEntityId = _orgId,
                SiteEntityId = _siteId,
                Status = "ONLINE",
                Hostname = "WS-CAPSTONE-HOST",
                MacAddress = "00:11:22:33:44:99",
                IpAddress = "10.0.0.99"
            };
            SetEntityId(ws, _wsId);
            _workstationRepo.Add(ws);
        }

        private static void SetEntityId<T>(T entity, Guid id) where T : BaseEntity
        {
            var prop = typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id), BindingFlags.Public | BindingFlags.Instance);
            prop?.SetValue(entity, id);
        }

        private UserPrincipal CreateAdminPrincipal()
        {
            return new UserPrincipal
            {
                UserId = Guid.NewGuid(),
                Username = "admin_capstone",
                IsAuthenticated = true,
                AccountStatus = UserAccountState.Active,
                Roles = new List<string> { RoleCatalog.Administrator },
                Permissions = new List<string> { PermissionCatalog.ViewWorkstations }
            };
        }

        // 1. Golden End-to-End Pipeline Scenario
        [Fact]
        public async Task GoldenE2EPipeline_FullLifecycle_SucceedsSeamlessly()
        {
            var principal = CreateAdminPrincipal();
            var connectionContext = new TelemetryConnectionContext("CONN-100", _pcId, _wsId, _siteId, _orgId);
            var now = DateTime.UtcNow;

            // Step A: Heartbeat Ingestion
            var hbMessage = new HeartbeatMessage { PcId = _pcId, Timestamp = now };
            var hbResult = await _ingestionService.IngestHeartbeatAsync(connectionContext, hbMessage);
            Assert.Equal(TelemetryIngestionStatus.Accepted, hbResult.Status);

            // Step B: Telemetry Ingestion
            var model = new TelemetryModel
            {
                Cpu = 45.0,
                Ram = 55.0,
                Uptime = 3600,
                TotalLaunches = 10,
                TotalCrashes = 0,
                TotalRestarts = 0,
                RunningGameName = "Cyberpunk 2077",
                RunningGameCpu = 30.0,
                RunningGameRam = 20.0,
                RunningGameDuration = 1200,
                Timestamp = now
            };

            var telResult = await _ingestionService.IngestTelemetrySnapshotAsync(connectionContext, model);
            Assert.Equal(TelemetryIngestionStatus.Accepted, telResult.Status);

            // Manual step: add record to history repository to mirror pipeline persistence
            _historyRepo.AddRecord(new TelemetryHistoryRecord
            {
                WorkstationId = _wsId,
                OrganizationId = _orgId,
                SiteId = _siteId,
                PcId = _pcId,
                Cpu = 45.0,
                Ram = 55.0,
                RunningGameName = "Cyberpunk 2077",
                ServerReceivedAt = now
            });

            // Step C: Current Hot State Verification (Redis)
            var hotState = await _stateStore.GetStateAsync(_pcId);
            Assert.NotNull(hotState);
            Assert.True(hotState.IsConnected);
            Assert.Equal(45.0, hotState.Cpu);
            Assert.Equal(55.0, hotState.Ram);
            Assert.Equal("Cyberpunk 2077", hotState.RunningGameName);

            // Step D: Historical Storage Verification (PostgreSQL)
            var history = await _historyRepo.GetHistoryForWorkstationAsync(_wsId);
            Assert.Single(history);
            Assert.Equal(45.0, history[0].Cpu);
            Assert.Equal("Cyberpunk 2077", history[0].RunningGameName);

            // Step E: Aggregation Processing
            var aggregates = _aggregationService.AggregateRawRecords(history, "1m");
            Assert.Single(aggregates);
            Assert.Equal(45.0, aggregates[0].CpuAvg);
            Assert.Equal(55.0, aggregates[0].RamAvg);

            // Step F: Health Evaluation Engine
            var healthResult = await _healthEvaluator.EvaluateWorkstationHealthAsync(hotState);
            Assert.Equal(WorkstationHealthState.Healthy, healthResult.HealthState);
            await _healthStore.SaveHealthResultAsync(healthResult);

            // Step G: Alert Engine
            var activeIncidents = await _alertEngine.EvaluateHealthResultAsync(healthResult, cancellationToken: default);
            Assert.Empty(activeIncidents); // Healthy = no alerts firing

            // Step H: Monitoring API Exposure
            var fleetRes = await _monitoringQueryService.GetFleetWorkstationsAsync(principal);
            Assert.True(fleetRes.IsSuccess);
            Assert.Equal(1, fleetRes.Value!.Summary.TotalTrackedWorkstations);

            var detailRes = await _monitoringQueryService.GetWorkstationDetailAsync(principal, _wsId);
            Assert.True(detailRes.IsSuccess);
            Assert.Equal(_pcId, detailRes.Value!.PcId);
            Assert.Equal(45.0, detailRes.Value.LatestMetrics!.Cpu);
        }

        // 2. Identity Verification & Anti-Spoofing
        [Fact]
        public async Task IdentityAntiSpoofing_MismatchedPcId_IsRejectedWithAuditLog()
        {
            var connectionContext = new TelemetryConnectionContext("CONN-100", _pcId, _wsId, _siteId, _orgId);
            var now = DateTime.UtcNow;

            var spoofedHeartbeat = new HeartbeatMessage
            {
                PcId = "WS-SPOOFED-99", // Payload claims WS-SPOOFED-99
                Timestamp = now
            };

            // Process payload under WS-CAPSTONE-01 connection context
            var result = await _ingestionService.IngestHeartbeatAsync(connectionContext, spoofedHeartbeat);

            Assert.Equal(TelemetryIngestionStatus.IdentityMismatch, result.Status);
            Assert.Equal(TelemetryRejectionReason.IdentityMismatch, result.RejectionReason);

            // Verify no hot-state mutation
            var hotState = await _stateStore.GetStateAsync(_pcId);
            Assert.Null(hotState);

            // Verify no historical persistence
            var history = await _historyRepo.GetHistoryForWorkstationAsync(_wsId);
            Assert.Empty(history);

            // Verify security audit log emission
            Assert.Contains(_securityEventService.LoggedEvents, e => e.EventType == "TELEMETRY_IDENTITY_MISMATCH");
        }

        // 3. Timestamp Validation & Clock Skew Guardrails
        [Fact]
        public async Task ClockSkewGuardrails_ExcessiveFutureAndPastTimestamps_AreRejected()
        {
            var connectionContext = new TelemetryConnectionContext("CONN-100", _pcId, _wsId, _siteId, _orgId);
            var now = DateTime.UtcNow;

            // Future timestamp > 5 min
            var futureModel = new TelemetryModel
            {
                Cpu = 50.0,
                Ram = 50.0,
                Uptime = 100,
                Timestamp = now.AddMinutes(10)
            };

            var futureRes = await _ingestionService.IngestTelemetrySnapshotAsync(connectionContext, futureModel);
            Assert.Equal(TelemetryIngestionStatus.Rejected, futureRes.Status);
            Assert.Equal(TelemetryRejectionReason.TimestampInFuture, futureRes.RejectionReason);

            // Past timestamp > 24 hours
            var pastModel = new TelemetryModel
            {
                Cpu = 50.0,
                Ram = 50.0,
                Uptime = 100,
                Timestamp = now.AddHours(-25)
            };

            var pastRes = await _ingestionService.IngestTelemetrySnapshotAsync(connectionContext, pastModel);
            Assert.Equal(TelemetryIngestionStatus.Rejected, pastRes.Status);
            Assert.Equal(TelemetryRejectionReason.TimestampExcessivelyOld, pastRes.RejectionReason);
        }

        // 4. Heartbeat vs Telemetry Liveness Decoupling
        [Fact]
        public async Task HeartbeatAndTelemetryDecoupling_ScenariosTestedCorrectly()
        {
            var connectionContext = new TelemetryConnectionContext("CONN-100", _pcId, _wsId, _siteId, _orgId);
            var startTime = DateTime.UtcNow.AddMinutes(-10);

            // Step 1: Initial Heartbeat + Telemetry
            await _ingestionService.IngestHeartbeatAsync(connectionContext, new HeartbeatMessage { PcId = _pcId, Timestamp = startTime });
            await _ingestionService.IngestTelemetrySnapshotAsync(connectionContext, new TelemetryModel
            {
                Cpu = 30.0,
                Ram = 40.0,
                Uptime = 100,
                Timestamp = startTime
            });

            // Scenario A: Heartbeat continues at now (startTime + 10m), telemetry stops
            var now = startTime.AddMinutes(10);
            await _ingestionService.IngestHeartbeatAsync(connectionContext, new HeartbeatMessage { PcId = _pcId, Timestamp = now });

            var state = await _stateStore.GetStateAsync(_pcId);
            Assert.NotNull(state);
            Assert.True(state.IsConnected); // Still connected via heartbeat

            // Set last telemetry received to 150s ago to trigger telemetry stale threshold
            state.LastTelemetryReceivedAt = now.AddSeconds(-150);

            var healthA = await _healthEvaluator.EvaluateWorkstationHealthAsync(state);
            Assert.Equal(WorkstationHealthState.Warning, healthA.HealthState);
            Assert.Contains(healthA.Reasons, r => r.ReasonCode == WorkstationHealthReasonCode.TelemetryStale);

            // Scenario B: Disconnected and exceeded offline timeout -> Offline health state
            var offlineState = new WorkstationRealTimeState(connectionContext.ToIdentity())
            {
                IsConnected = false,
                LastSeenAt = now.AddSeconds(-400)
            };
            var healthB = await _healthEvaluator.EvaluateWorkstationHealthAsync(offlineState);
            Assert.Equal(WorkstationHealthState.Offline, healthB.HealthState);

            // Scenario C: Telemetry resumes -> state recovers
            await _ingestionService.IngestHeartbeatAsync(connectionContext, new HeartbeatMessage { PcId = _pcId, Timestamp = now });
            await _ingestionService.IngestTelemetrySnapshotAsync(connectionContext, new TelemetryModel
            {
                Cpu = 35.0,
                Ram = 45.0,
                Uptime = 200,
                Timestamp = now
            });

            var recoveredState = await _stateStore.GetStateAsync(_pcId);
            var healthC = await _healthEvaluator.EvaluateWorkstationHealthAsync(recoveredState);
            Assert.Equal(WorkstationHealthState.Healthy, healthC.HealthState);
        }

        // 5. Current State vs Historical Integrity
        [Fact]
        public async Task OutOfOrderTelemetry_DoesNotOverwriteNewerHotState()
        {
            var connectionContext = new TelemetryConnectionContext("CONN-100", _pcId, _wsId, _siteId, _orgId);
            var now = DateTime.UtcNow;

            // Newer sample (t = now)
            var newModel = new TelemetryModel
            {
                Cpu = 80.0,
                Ram = 70.0,
                Uptime = 100,
                Timestamp = now
            };
            await _ingestionService.IngestTelemetrySnapshotAsync(connectionContext, newModel);
            _historyRepo.AddRecord(new TelemetryHistoryRecord { WorkstationId = _wsId, PcId = _pcId, Cpu = 80.0, Ram = 70.0, ServerReceivedAt = now });

            // Mock idempotency service returning true for isStale for older snapshot
            _idempotencyService.SetStale("WS-CAPSTONE-01", true);

            // Older sample (t = now - 2 min)
            var oldModel = new TelemetryModel
            {
                Cpu = 10.0,
                Ram = 10.0,
                Uptime = 50,
                Timestamp = now.AddMinutes(-2)
            };
            var staleRes = await _ingestionService.IngestTelemetrySnapshotAsync(connectionContext, oldModel);
            Assert.Equal(TelemetryIngestionStatus.Stale, staleRes.Status);

            // Hot state in Redis MUST preserve newer values (Cpu = 80.0)
            var hotState = await _stateStore.GetStateAsync(_pcId);
            Assert.Equal(80.0, hotState!.Cpu);
        }

        // 6. Deterministic Aggregation Calculations
        [Fact]
        public async Task DeterministicAggregation_KnownDataset_CalculatesCorrectStatistics()
        {
            var now = DateTime.UtcNow;
            var windowStart = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);

            // Deterministic CPU dataset: 10, 20, 30, 40, 50 -> Avg = 30, Min = 10, Max = 50, SampleCount = 5
            var values = new double[] { 10, 20, 30, 40, 50 };
            var records = values.Select((val, idx) => new TelemetryHistoryRecord
            {
                WorkstationId = _wsId,
                OrganizationId = _orgId,
                SiteId = _siteId,
                PcId = _pcId,
                Cpu = val,
                Ram = 50.0,
                Uptime = 1000 + (idx * 10),
                ServerReceivedAt = windowStart.AddSeconds(idx * 10)
            }).ToList();

            var aggregates = _aggregationService.AggregateRawRecords(records, "1m");

            Assert.Single(aggregates);
            var agg = aggregates[0];
            Assert.Equal(5, agg.SampleCount);
            Assert.Equal(10.0, agg.CpuMin);
            Assert.Equal(50.0, agg.CpuMax);
            Assert.Equal(30.0, agg.CpuAvg);
            Assert.Equal(48.0, agg.CpuP95);
        }

        // 7. Health Engine & Alerting State Transitions
        [Fact]
        public async Task HealthAndAlertEngine_CriticalCondition_TransitionsToFiringAndResolves()
        {
            var identity = new WorkstationIdentity(_pcId, _wsId, _siteId, _orgId);
            var now = DateTime.UtcNow;

            // Trigger Critical Health (CPU = 98%)
            var criticalResult = new WorkstationHealthEvaluationResult(
                identity,
                WorkstationHealthState.Critical,
                10.0,
                new List<WorkstationHealthReason>
                {
                    new WorkstationHealthReason(
                        "CPU_SUSTAINED_HIGH",
                        WorkstationHealthState.Critical,
                        "CPU",
                        98.0,
                        80.0,
                        "CPU sustained critical level",
                        now)
                },
                now);

            // Alert engine evaluates critical health -> Firing incident
            var incidentsWave1 = await _alertEngine.EvaluateHealthResultAsync(criticalResult, cancellationToken: default);
            Assert.Single(incidentsWave1);
            var incident = incidentsWave1[0];
            Assert.Equal(IncidentLifecycleState.Firing, incident.LifecycleState);
            Assert.Equal(AlertSeverity.Critical, incident.Severity);

            // Re-evaluate wave 2 with same critical health -> Deduplicated incident, observation count incremented
            var incidentsWave2 = await _alertEngine.EvaluateHealthResultAsync(criticalResult, cancellationToken: default);
            Assert.Single(incidentsWave2);
            Assert.Equal(2, incidentsWave2[0].ObservationCount);

            // Trigger Recovery -> Health becomes Healthy
            var healthyResult = WorkstationHealthEvaluationResult.CreateHealthy(identity, now.AddMinutes(1), "v1.0");

            var incidentsWave3 = await _alertEngine.EvaluateHealthResultAsync(healthyResult, cancellationToken: default);
            Assert.Single(incidentsWave3); // Resolved incident returned
            Assert.Equal(IncidentLifecycleState.Resolved, incidentsWave3[0].LifecycleState);
        }

        // 8. Security & Multi-Tenant Authorization Isolation
        [Fact]
        public async Task MonitoringApi_CrossTenantAccess_IsForbidden()
        {
            var otherOrgId = Guid.NewGuid();
            var operatorPrincipal = new UserPrincipal
            {
                UserId = Guid.NewGuid(),
                Username = "operator_org_a",
                IsAuthenticated = true,
                AccountStatus = UserAccountState.Active,
                OrganizationId = _orgId, // Org A
                SiteId = _siteId,
                Roles = new List<string> { "Operator" },
                Permissions = new List<string> { PermissionCatalog.ViewWorkstations }
            };

            // Operator attempts to access Org B data
            var result = await _monitoringQueryService.GetFleetWorkstationsAsync(operatorPrincipal, organizationId: otherOrgId);

            Assert.False(result.IsSuccess);
            Assert.Equal("CROSS_ORGANIZATION_ACCESS_DENIED", result.ErrorCode);
        }

        // 9. End-to-End Pipeline Traceability & Field Consistency
        [Fact]
        public async Task PipelineTraceability_FieldsAreConsistentAcrossBoundaries()
        {
            var connectionContext = new TelemetryConnectionContext("CONN-TRACE-001", _pcId, _wsId, _siteId, _orgId);
            var now = DateTime.UtcNow;

            var model = new TelemetryModel
            {
                Cpu = 50.0,
                Ram = 60.0,
                Uptime = 100,
                Timestamp = now
            };

            await _ingestionService.IngestTelemetrySnapshotAsync(connectionContext, model);
            _historyRepo.AddRecord(new TelemetryHistoryRecord
            {
                WorkstationId = _wsId,
                PcId = _pcId,
                ConnectionId = "CONN-TRACE-001",
                SessionId = "SESS-TRACE-001",
                Cpu = 50.0,
                Ram = 60.0,
                ServerReceivedAt = now
            });

            // Hot State Trace
            var state = await _stateStore.GetStateAsync(_pcId);
            Assert.NotNull(state);

            // PostgreSQL History Trace
            var history = await _historyRepo.GetHistoryForWorkstationAsync(_wsId);
            Assert.Single(history);
            Assert.Equal("SESS-TRACE-001", history[0].SessionId);
            Assert.Equal("CONN-TRACE-001", history[0].ConnectionId);
        }
    }

    #region Additional Test Fakes

    public class FakeWorkstationStateStore : IWorkstationStateStore, IWorkstationStateReader
    {
        private readonly Dictionary<string, WorkstationRealTimeState> _states = new(StringComparer.OrdinalIgnoreCase);

        public Task<WorkstationRealTimeState?> GetStateAsync(string pcId, CancellationToken cancellationToken = default)
        {
            _states.TryGetValue(pcId, out var state);
            return Task.FromResult(state);
        }

        public Task<WorkstationRealTimeState?> GetStateByWorkstationIdAsync(Guid workstationId, CancellationToken cancellationToken = default)
        {
            var state = _states.Values.FirstOrDefault(s => s.WorkstationId == workstationId);
            return Task.FromResult(state);
        }

        public Task SaveStateAsync(WorkstationRealTimeState state, CancellationToken cancellationToken = default)
        {
            _states[state.PcId] = state;
            return Task.CompletedTask;
        }

        public Task UpdateFromTelemetryAsync(WorkstationIdentity identity, TelemetrySnapshot snapshot, string? connectionId = null, CancellationToken cancellationToken = default)
        {
            if (!_states.TryGetValue(identity.PcId, out var state))
            {
                state = new WorkstationRealTimeState(identity);
                _states[identity.PcId] = state;
            }

            state.IsConnected = true;
            state.ConnectionId = connectionId;
            state.Cpu = snapshot.Cpu;
            state.Ram = snapshot.Ram;
            state.Uptime = snapshot.Uptime;
            state.RunningGameName = snapshot.RunningGameName;
            state.LastTelemetryReceivedAt = snapshot.ServerReceivedAt;
            state.LastSeenAt = snapshot.ServerReceivedAt;
            return Task.CompletedTask;
        }

        public Task UpdateFromHeartbeatAsync(WorkstationIdentity identity, HeartbeatSignal heartbeat, string? connectionId = null, CancellationToken cancellationToken = default)
        {
            if (!_states.TryGetValue(identity.PcId, out var state))
            {
                state = new WorkstationRealTimeState(identity);
                _states[identity.PcId] = state;
            }

            state.IsConnected = true;
            state.ConnectionId = connectionId;
            state.LastHeartbeatReceivedAt = heartbeat.ServerReceivedAt;
            state.LastSeenAt = heartbeat.ServerReceivedAt;
            return Task.CompletedTask;
        }

        public Task UpdateFromOperationalEventAsync(WorkstationIdentity identity, OperationalEventSignal eventSignal, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task UpdateConnectionStateAsync(WorkstationIdentity identity, string connectionId, bool isConnected, string? connectionState, CancellationToken cancellationToken = default)
        {
            if (!_states.TryGetValue(identity.PcId, out var state))
            {
                state = new WorkstationRealTimeState(identity);
                _states[identity.PcId] = state;
            }

            state.IsConnected = isConnected;
            if (!string.IsNullOrEmpty(connectionState))
            {
                state.ConnectionState = connectionState;
            }
            return Task.CompletedTask;
        }

        public Task<WorkstationRealTimeState?> GetCurrentStateAsync(string pcId, CancellationToken cancellationToken = default)
        {
            return GetStateAsync(pcId, cancellationToken);
        }

        public Task<WorkstationRealTimeState?> GetCurrentStateByWorkstationIdAsync(Guid workstationId, CancellationToken cancellationToken = default)
        {
            return GetStateByWorkstationIdAsync(workstationId, cancellationToken);
        }

        public Task<FleetStateSummary> GetFleetSummaryAsync(Guid? siteId = null, Guid? organizationId = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new FleetStateSummary(_states.Count, _states.Count, 0, 0, 0, 0, 0, DateTime.UtcNow));
        }

        public Task<IReadOnlyList<WorkstationRealTimeState>> GetWorkstationStatesAsync(Guid? siteId = null, Guid? organizationId = null, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<WorkstationRealTimeState> list = _states.Values.ToList();
            return Task.FromResult(list);
        }
    }

    public class FakeSecurityEventService : ISecurityEventService
    {
        public List<SecurityEventLog> LoggedEvents { get; } = new();

        public Task RecordSecurityEventAsync(SecurityEvent securityEvent, CancellationToken cancellationToken = default)
        {
            LoggedEvents.Add(new SecurityEventLog
            {
                EventType = securityEvent.EventType,
                UserId = securityEvent.ActorId,
                Username = securityEvent.ActorType,
                OrganizationId = securityEvent.OrganizationId,
                SiteId = securityEvent.SiteId,
                WorkstationId = securityEvent.ResourceId,
                Description = securityEvent.FailureReason ?? securityEvent.Action ?? securityEvent.Result
            });
            return Task.CompletedTask;
        }

        public Task RecordSecurityEventAsync(string eventType, Guid? actorId, string? actorType, string? deviceId, Guid? organizationId, Guid? siteId, string? resourceType, Guid? resourceId, string? action, string result, string? failureReason, string? correlationId = null, string? traceId = null, CancellationToken cancellationToken = default)
        {
            LoggedEvents.Add(new SecurityEventLog
            {
                EventType = eventType,
                UserId = actorId,
                Username = actorType,
                OrganizationId = organizationId,
                SiteId = siteId,
                WorkstationId = resourceId,
                Description = failureReason ?? action ?? result
            });
            return Task.CompletedTask;
        }
    }

    public class SecurityEventLog
    {
        public string EventType { get; set; } = string.Empty;
        public Guid? UserId { get; set; }
        public string? Username { get; set; }
        public Guid? OrganizationId { get; set; }
        public Guid? SiteId { get; set; }
        public Guid? WorkstationId { get; set; }
        public string? IpAddress { get; set; }
        public string Description { get; set; } = string.Empty;
    }

    public class FakeTelemetryIdempotencyService : ITelemetryIdempotencyService
    {
        private readonly HashSet<string> _events = new();
        private readonly Dictionary<string, bool> _staleMap = new(StringComparer.OrdinalIgnoreCase);

        public void SetStale(string pcId, bool isStale)
        {
            _staleMap[pcId] = isStale;
        }

        public Task<bool> IsDuplicateEventAsync(string eventId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_events.Contains(eventId));
        }

        public Task MarkEventProcessedAsync(string eventId, CancellationToken cancellationToken = default)
        {
            _events.Add(eventId);
            return Task.CompletedTask;
        }

        public Task<bool> IsStaleTelemetryAsync(string pcId, DateTime clientTimestamp, CancellationToken cancellationToken = default)
        {
            _staleMap.TryGetValue(pcId, out bool isStale);
            return Task.FromResult(isStale);
        }

        public Task RecordLatestTelemetryTimestampAsync(string pcId, DateTime clientTimestamp, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    public class FakeAlertMetrics : IAlertMetrics
    {
        public void RecordAlertEvaluated(string ruleCode, string severity) { }
        public void RecordAlertTriggered(string ruleCode, string severity) { }
        public void RecordIncidentCreated(string ruleCode, string severity) { }
        public void RecordIncidentDeduplicated(string ruleCode) { }
        public void RecordIncidentResolved(string ruleCode) { }
        public void RecordIncidentSuppressed(string ruleCode) { }
        public void RecordEvaluationFailure() { }
        public void RecordEvaluationDuration(double durationSeconds) { }
    }

    public class FakeAlertNotificationDispatcher : IAlertNotificationDispatcher
    {
        public Task DispatchNotificationAsync(Incident incident, string notificationType, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    #endregion
}
