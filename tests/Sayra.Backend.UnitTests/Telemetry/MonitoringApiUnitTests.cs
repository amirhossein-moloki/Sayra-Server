using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Security;
using Sayra.Backend.Application.Telemetry;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Domain.Enums;
using Sayra.Backend.Domain.Telemetry;
using Sayra.Backend.Domain.ValueObjects;
using Xunit;

namespace Sayra.Backend.UnitTests.Telemetry
{
    public class MonitoringApiUnitTests
    {
        private readonly FakeAuthorizationService _authService;
        private readonly FakeWorkstationRepository _workstationRepo;
        private readonly FakeWorkstationStateReader _stateReader;
        private readonly FakeWorkstationHealthStore _healthStore;
        private readonly FakeIncidentRepository _incidentRepo;
        private readonly FakeTelemetryHistoryRepository _historyRepo;
        private readonly FakeTelemetryAggregateRepository _aggregateRepo;
        private readonly FakeAuditEventRepository _auditEventRepo;
        private readonly MonitoringQueryService _service;

        private readonly Guid _orgId = Guid.NewGuid();
        private readonly Guid _siteId = Guid.NewGuid();
        private readonly Guid _otherOrgId = Guid.NewGuid();
        private readonly Guid _otherSiteId = Guid.NewGuid();

        public MonitoringApiUnitTests()
        {
            _authService = new FakeAuthorizationService();
            _workstationRepo = new FakeWorkstationRepository();
            _stateReader = new FakeWorkstationStateReader();
            _healthStore = new FakeWorkstationHealthStore();
            _incidentRepo = new FakeIncidentRepository();
            _historyRepo = new FakeTelemetryHistoryRepository();
            _aggregateRepo = new FakeTelemetryAggregateRepository();
            _auditEventRepo = new FakeAuditEventRepository();

            _service = new MonitoringQueryService(
                _authService,
                _workstationRepo,
                _stateReader,
                _healthStore,
                _healthStore,
                _incidentRepo,
                _historyRepo,
                _aggregateRepo,
                _auditEventRepo);
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
                Username = "admin",
                IsAuthenticated = true,
                AccountStatus = UserAccountState.Active,
                Roles = new List<string> { RoleCatalog.Administrator },
                Permissions = new List<string> { PermissionCatalog.ViewWorkstations }
            };
        }

        private UserPrincipal CreateOperatorPrincipal(Guid? orgId = null, Guid? siteId = null)
        {
            return new UserPrincipal
            {
                UserId = Guid.NewGuid(),
                Username = "operator",
                IsAuthenticated = true,
                AccountStatus = UserAccountState.Active,
                OrganizationId = orgId ?? _orgId,
                SiteId = siteId ?? _siteId,
                Roles = new List<string> { "Operator" },
                Permissions = new List<string> { PermissionCatalog.ViewWorkstations }
            };
        }

        [Fact]
        public async Task UnauthenticatedUser_ReturnsUnauthorized()
        {
            var principal = new UserPrincipal { IsAuthenticated = false };

            var result = await _service.GetFleetWorkstationsAsync(principal);

            Assert.False(result.IsSuccess);
            Assert.Equal("UNAUTHORIZED", result.ErrorCode);
        }

        [Fact]
        public async Task DisabledUser_ReturnsAccountDisabled()
        {
            var principal = CreateAdminPrincipal();
            principal.AccountStatus = UserAccountState.Disabled;

            var result = await _service.GetFleetWorkstationsAsync(principal);

            Assert.False(result.IsSuccess);
            Assert.Equal("ACCOUNT_DISABLED", result.ErrorCode);
        }

        [Fact]
        public async Task MissingPermission_ReturnsPermissionDenied()
        {
            var principal = CreateAdminPrincipal();
            principal.Permissions.Clear();
            _authService.SetDenied("Permission 'ViewWorkstations' is required.", "PERMISSION_DENIED");

            var result = await _service.GetFleetWorkstationsAsync(principal);

            Assert.False(result.IsSuccess);
            Assert.Equal("PERMISSION_DENIED", result.ErrorCode);
        }

        [Fact]
        public async Task CrossOrganizationAccess_ReturnsCrossOrganizationAccessDenied()
        {
            var principal = CreateOperatorPrincipal(_orgId, _siteId);

            var result = await _service.GetFleetWorkstationsAsync(principal, organizationId: _otherOrgId);

            Assert.False(result.IsSuccess);
            Assert.Equal("CROSS_ORGANIZATION_ACCESS_DENIED", result.ErrorCode);
        }

        [Fact]
        public async Task CrossSiteAccess_ReturnsCrossSiteAccessDenied()
        {
            var principal = CreateOperatorPrincipal(_orgId, _siteId);

            var result = await _service.GetFleetWorkstationsAsync(principal, siteId: _otherSiteId);

            Assert.False(result.IsSuccess);
            Assert.Equal("CROSS_SITE_ACCESS_DENIED", result.ErrorCode);
        }

        [Fact]
        public async Task GetFleetWorkstations_ReturnsCorrectSummaryAndItems()
        {
            var principal = CreateAdminPrincipal();

            var ws1 = new Workstation { PcId = "WS-001", Name = "Workstation 1", OrganizationEntityId = _orgId, SiteEntityId = _siteId, Status = "ONLINE", Hostname = "HOST1", MacAddress = "00:11:22:33:44:55", IpAddress = "192.168.1.10" };
            var ws2 = new Workstation { PcId = "WS-002", Name = "Workstation 2", OrganizationEntityId = _orgId, SiteEntityId = _siteId, Status = "OFFLINE", Hostname = "HOST2", MacAddress = "00:11:22:33:44:56", IpAddress = "192.168.1.11" };
            _workstationRepo.Add(ws1);
            _workstationRepo.Add(ws2);

            _stateReader.SetState(new WorkstationRealTimeState(new WorkstationIdentity("WS-001", ws1.Id, _siteId, _orgId))
            {
                IsConnected = true,
                ConnectionState = "Active",
                Cpu = 25.0,
                Ram = 40.0
            });

            _stateReader.SetState(new WorkstationRealTimeState(new WorkstationIdentity("WS-002", ws2.Id, _siteId, _orgId))
            {
                IsConnected = false,
                ConnectionState = "Disconnected"
            });

            _healthStore.SetHealth(new WorkstationHealthEvaluationResult(
                new WorkstationIdentity("WS-001", ws1.Id, _siteId, _orgId),
                WorkstationHealthState.Healthy,
                100.0,
                new List<WorkstationHealthReason>(),
                DateTime.UtcNow));

            _healthStore.SetHealth(new WorkstationHealthEvaluationResult(
                new WorkstationIdentity("WS-002", ws2.Id, _siteId, _orgId),
                WorkstationHealthState.Offline,
                0.0,
                new List<WorkstationHealthReason>(),
                DateTime.UtcNow));

            var result = await _service.GetFleetWorkstationsAsync(principal);

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal(2, result.Value.TotalCount);
            Assert.Equal(2, result.Value.Summary.TotalTrackedWorkstations);
            Assert.Equal(1, result.Value.Summary.OnlineCount);
            Assert.Equal(1, result.Value.Summary.OfflineCount);
            Assert.Equal(1, result.Value.Summary.HealthyCount);
        }

        [Fact]
        public async Task GetWorkstationDetail_ExistingWorkstation_ReturnsWorkstationDetail()
        {
            var principal = CreateAdminPrincipal();
            var ws = new Workstation { PcId = "WS-100", Name = "Gaming Rig 100", OrganizationEntityId = _orgId, SiteEntityId = _siteId, Status = "ONLINE", Hostname = "RIG100", MacAddress = "AA:BB:CC:DD:EE:FF", IpAddress = "10.0.0.100" };
            _workstationRepo.Add(ws);

            _stateReader.SetState(new WorkstationRealTimeState(new WorkstationIdentity("WS-100", ws.Id, _siteId, _orgId))
            {
                IsConnected = true,
                ConnectionState = "Active",
                Cpu = 45.5,
                Ram = 62.0,
                Uptime = 7200,
                TotalLaunches = 15,
                TotalCrashes = 1,
                TotalRestarts = 0,
                RunningGameName = "Cyberpunk 2077",
                RunningGameCpu = 35.0,
                RunningGameRam = 24.0,
                RunningGameDuration = 1800,
                LastTelemetryReceivedAt = DateTime.UtcNow
            });

            var result = await _service.GetWorkstationDetailAsync(principal, ws.Id);

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal(ws.Id, result.Value.WorkstationId);
            Assert.Equal("WS-100", result.Value.PcId);
            Assert.True(result.Value.IsConnected);
            Assert.NotNull(result.Value.LatestMetrics);
            Assert.Equal(45.5, result.Value.LatestMetrics.Cpu);
            Assert.Equal("Cyberpunk 2077", result.Value.LatestMetrics.RunningGameId);
        }

        [Fact]
        public async Task GetWorkstationDetail_NonExistentWorkstation_ReturnsNotFound()
        {
            var principal = CreateAdminPrincipal();
            var result = await _service.GetWorkstationDetailAsync(principal, Guid.NewGuid());

            Assert.False(result.IsSuccess);
            Assert.Equal("WORKSTATION_NOT_FOUND", result.ErrorCode);
        }

        [Fact]
        public async Task GetWorkstationHealth_ReturnsHealthDetail()
        {
            var principal = CreateAdminPrincipal();
            var ws = new Workstation { PcId = "WS-HEALTH-1", Name = "Health Test WS", OrganizationEntityId = _orgId, SiteEntityId = _siteId, Status = "ONLINE", Hostname = "HWS1", MacAddress = "11:22:33:44:55:66", IpAddress = "10.0.0.50" };
            _workstationRepo.Add(ws);

            var healthRes = new WorkstationHealthEvaluationResult(
                new WorkstationIdentity("WS-HEALTH-1", ws.Id, _siteId, _orgId),
                WorkstationHealthState.Warning,
                85.0,
                new List<WorkstationHealthReason>
                {
                    new WorkstationHealthReason("CPU_HIGH", WorkstationHealthState.Warning, "CPU", 88.0, 80.0, "CPU usage elevated", DateTime.UtcNow)
                },
                DateTime.UtcNow);

            _healthStore.SetHealth(healthRes);

            var result = await _service.GetWorkstationHealthAsync(principal, ws.Id);

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal(WorkstationHealthState.Warning, result.Value.HealthState);
            Assert.Equal(85.0, result.Value.HealthScore);
            Assert.Single(result.Value.Reasons);
            Assert.Equal("CPU_HIGH", result.Value.Reasons[0].ReasonCode);
        }

        [Fact]
        public async Task GetWorkstationMetrics_EndBeforeStart_ReturnsInvalidRange()
        {
            var principal = CreateAdminPrincipal();
            var ws = new Workstation { PcId = "WS-RANGE", Name = "Range WS", OrganizationEntityId = _orgId, SiteEntityId = _siteId, Hostname = "RWS", MacAddress = "00:00:00:00:00:01", IpAddress = "10.0.0.1" };
            _workstationRepo.Add(ws);

            var now = DateTime.UtcNow;
            var start = now;
            var end = now.AddHours(-1); // Invalid: end < start

            var result = await _service.GetWorkstationMetricsAsync(principal, ws.Id, start: start, end: end);

            Assert.False(result.IsSuccess);
            Assert.Equal("INVALID_RANGE", result.ErrorCode);
        }

        [Fact]
        public async Task GetWorkstationMetrics_ExceedsMaxRange_ReturnsInvalidRange()
        {
            var principal = CreateAdminPrincipal();
            var ws = new Workstation { PcId = "WS-MAX-RANGE", Name = "Max Range WS", OrganizationEntityId = _orgId, SiteEntityId = _siteId, Hostname = "MRWS", MacAddress = "00:00:00:00:00:02", IpAddress = "10.0.0.2" };
            _workstationRepo.Add(ws);

            var start = DateTime.UtcNow.AddDays(-10);
            var end = DateTime.UtcNow;

            // Raw resolution max allowed range is 7 days
            var result = await _service.GetWorkstationMetricsAsync(principal, ws.Id, start: start, end: end, resolution: "raw");

            Assert.False(result.IsSuccess);
            Assert.Equal("INVALID_RANGE", result.ErrorCode);
        }

        [Fact]
        public async Task GetWorkstationMetrics_InvalidResolution_ReturnsInvalidResolution()
        {
            var principal = CreateAdminPrincipal();
            var ws = new Workstation { PcId = "WS-RES", Name = "Resolution WS", OrganizationEntityId = _orgId, SiteEntityId = _siteId, Hostname = "RESWS", MacAddress = "00:00:00:00:00:03", IpAddress = "10.0.0.3" };
            _workstationRepo.Add(ws);

            var result = await _service.GetWorkstationMetricsAsync(principal, ws.Id, resolution: "invalid_res");

            Assert.False(result.IsSuccess);
            Assert.Equal("INVALID_RESOLUTION", result.ErrorCode);
        }

        [Fact]
        public async Task GetWorkstationMetrics_RawResolution_QueriesHistoryRepository()
        {
            var principal = CreateAdminPrincipal();
            var ws = new Workstation { PcId = "WS-RAW", Name = "Raw WS", OrganizationEntityId = _orgId, SiteEntityId = _siteId, Hostname = "RAWWS", MacAddress = "00:00:00:00:00:04", IpAddress = "10.0.0.4" };
            _workstationRepo.Add(ws);

            var now = DateTime.UtcNow;
            _historyRepo.AddRecord(new TelemetryHistoryRecord
            {
                WorkstationId = ws.Id,
                PcId = "WS-RAW",
                Cpu = 50.0,
                Ram = 60.0,
                Uptime = 3600,
                ServerReceivedAt = now.AddMinutes(-10)
            });

            var result = await _service.GetWorkstationMetricsAsync(principal, ws.Id, resolution: "raw");

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal("raw", result.Value.Resolution);
            Assert.Single(result.Value.Points);
            Assert.Equal(50.0, result.Value.Points[0].Cpu);
        }

        [Fact]
        public async Task GetWorkstationMetrics_AggregatedResolution_QueriesAggregateRepository()
        {
            var principal = CreateAdminPrincipal();
            var ws = new Workstation { PcId = "WS-AGG", Name = "Agg WS", OrganizationEntityId = _orgId, SiteEntityId = _siteId, Hostname = "AGGWS", MacAddress = "00:00:00:00:00:05", IpAddress = "10.0.0.5" };
            _workstationRepo.Add(ws);

            var now = DateTime.UtcNow;
            _aggregateRepo.AddRecord(new TelemetryAggregateRecord
            {
                WorkstationId = ws.Id,
                PcId = "WS-AGG",
                Granularity = "1m",
                WindowStart = now.AddMinutes(-5),
                WindowEnd = now.AddMinutes(-4),
                CpuAvg = 42.5,
                RamAvg = 55.0,
                SampleCount = 12
            });

            var start = now.AddHours(-1);
            var end = now;

            var result = await _service.GetWorkstationMetricsAsync(principal, ws.Id, start: start, end: end, resolution: "1m");

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal("1m", result.Value.Resolution);
            Assert.Single(result.Value.Points);
            Assert.Equal(42.5, result.Value.Points[0].Cpu);
        }

        [Fact]
        public async Task GetMonitoringEvents_FiltersAndPagesCorrectly()
        {
            var principal = CreateAdminPrincipal();
            var wsId = Guid.NewGuid();

            _auditEventRepo.Add(new AuditEvent
            {
                EventId = Guid.NewGuid(),
                EventType = "CONFIG_PUBLISHED",
                WorkstationId = wsId,
                Timestamp = DateTime.UtcNow.AddMinutes(-5),
                Payload = "{\"version\":1}"
            });

            _auditEventRepo.Add(new AuditEvent
            {
                EventId = Guid.NewGuid(),
                EventType = "UPDATE_COMPLETED",
                WorkstationId = wsId,
                Timestamp = DateTime.UtcNow.AddMinutes(-2),
                Payload = "{\"packageId\":\"P1\"}"
            });

            var result = await _service.GetMonitoringEventsAsync(principal, workstationId: wsId, eventType: "UPDATE_COMPLETED");

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal(1, result.Value.TotalCount);
            Assert.Single(result.Value.Items);
            Assert.Equal("UPDATE_COMPLETED", result.Value.Items[0].EventType);
        }

        [Fact]
        public async Task GetIncidents_QueriesAndPagesCorrectly()
        {
            var principal = CreateAdminPrincipal();
            var wsId = Guid.NewGuid();

            var incident = Incident.CreateFiring(
                fingerprint: "FINGERPRINT-1",
                ruleCode: "CPU_SUSTAINED_HIGH",
                organizationId: _orgId,
                siteId: _siteId,
                workstationId: wsId,
                pcId: "WS-INC-1",
                severity: AlertSeverity.Critical,
                reasonCode: "CPU_CRITICAL",
                source: "WorkstationHealthEvaluator",
                title: "Critical CPU",
                description: "CPU exceeded 95% for > 60s",
                triggerEvidence: "{\"cpu\":97.5}",
                firingAtUtc: DateTime.UtcNow);

            _incidentRepo.Add(incident);

            var result = await _service.GetIncidentsAsync(principal, severity: AlertSeverity.Critical);

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal(1, result.Value.TotalCount);
            Assert.Single(result.Value.Items);
            Assert.Equal("CPU_SUSTAINED_HIGH", result.Value.Items[0].RuleName);
            Assert.Equal(AlertSeverity.Critical, result.Value.Items[0].Severity);
        }

        [Fact]
        public async Task GetIncidentDetail_ExistingIncident_ReturnsDetail()
        {
            var principal = CreateAdminPrincipal();

            var incident = Incident.CreateFiring(
                fingerprint: "FINGERPRINT-DETAIL",
                ruleCode: "MEMORY_HIGH",
                organizationId: _orgId,
                siteId: _siteId,
                workstationId: Guid.NewGuid(),
                pcId: "WS-INC-DETAIL",
                severity: AlertSeverity.Warning,
                reasonCode: "RAM_WARNING",
                source: "WorkstationHealthEvaluator",
                title: "Memory Warning",
                description: "RAM usage elevated",
                triggerEvidence: "{\"ram\":88.0}",
                firingAtUtc: DateTime.UtcNow);

            _incidentRepo.Add(incident);

            var result = await _service.GetIncidentDetailAsync(principal, incident.Id);

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal(incident.Id, result.Value.Id);
            Assert.Equal("MEMORY_HIGH", result.Value.RuleName);
            Assert.Equal("Memory Warning", result.Value.Summary);
        }

        [Fact]
        public async Task GetIncidentDetail_NonExistentIncident_ReturnsNotFound()
        {
            var principal = CreateAdminPrincipal();
            var result = await _service.GetIncidentDetailAsync(principal, Guid.NewGuid());

            Assert.False(result.IsSuccess);
            Assert.Equal("INCIDENT_NOT_FOUND", result.ErrorCode);
        }
    }

    #region Test Fakes

    public class FakeAuthorizationService : IAuthorizationService
    {
        private bool _isDenied = false;
        private string _failureReason = "Access denied.";
        private string _errorCode = "PERMISSION_DENIED";

        public void SetDenied(string reason, string code)
        {
            _isDenied = true;
            _failureReason = reason;
            _errorCode = code;
        }

        public Task<AuthorizationResult> AuthorizeAsync(UserPrincipal? principal, string permission, object? resource = null, CancellationToken cancellationToken = default)
        {
            if (principal == null || !principal.IsAuthenticated)
            {
                return Task.FromResult(AuthorizationResult.Denied("User is not authenticated.", "UNAUTHORIZED"));
            }

            if (_isDenied)
            {
                return Task.FromResult(AuthorizationResult.Denied(_failureReason, _errorCode));
            }

            return Task.FromResult(AuthorizationResult.Allowed());
        }
    }

    public class FakeWorkstationRepository : IRepository<Workstation>
    {
        private readonly List<Workstation> _items = new();

        public void Add(Workstation item) => _items.Add(item);

        public Task AddAsync(Workstation entity, CancellationToken cancellationToken = default)
        {
            _items.Add(entity);
            return Task.CompletedTask;
        }

        public void Delete(Workstation entity) => _items.Remove(entity);

        public Task<IReadOnlyList<Workstation>> FindAsync(Expression<Func<Workstation, bool>> predicate, bool track = true, CancellationToken cancellationToken = default)
        {
            var compiled = predicate.Compile();
            IReadOnlyList<Workstation> result = _items.Where(compiled).ToList();
            return Task.FromResult(result);
        }

        public Task<Workstation?> FirstOrDefaultAsync(Expression<Func<Workstation, bool>> predicate, bool track = true, CancellationToken cancellationToken = default)
        {
            var compiled = predicate.Compile();
            return Task.FromResult(_items.FirstOrDefault(compiled));
        }

        public Task<IReadOnlyList<Workstation>> GetAllAsync(bool track = true, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<Workstation> result = _items.ToList();
            return Task.FromResult(result);
        }

        public Task<Workstation?> GetByIdAsync(Guid id, bool track = true, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_items.FirstOrDefault(x => x.Id == id));
        }

        public void Update(Workstation entity) { }
    }

    public class FakeWorkstationStateReader : IWorkstationStateReader
    {
        private readonly Dictionary<string, WorkstationRealTimeState> _states = new(StringComparer.OrdinalIgnoreCase);

        public void SetState(WorkstationRealTimeState state)
        {
            _states[state.PcId] = state;
        }

        public Task<WorkstationRealTimeState?> GetCurrentStateAsync(string pcId, CancellationToken cancellationToken = default)
        {
            _states.TryGetValue(pcId, out var state);
            return Task.FromResult(state);
        }

        public Task<WorkstationRealTimeState?> GetCurrentStateByWorkstationIdAsync(Guid workstationId, CancellationToken cancellationToken = default)
        {
            var state = _states.Values.FirstOrDefault(s => s.WorkstationId == workstationId);
            return Task.FromResult(state);
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

    public class FakeWorkstationHealthStore : IWorkstationHealthStore, IWorkstationHealthReader
    {
        private readonly Dictionary<string, WorkstationHealthEvaluationResult> _results = new(StringComparer.OrdinalIgnoreCase);

        public void SetHealth(WorkstationHealthEvaluationResult result)
        {
            _results[result.Identity.PcId] = result;
        }

        public Task<IReadOnlyList<WorkstationHealthEvaluationResult>> GetFleetHealthResultsAsync(Guid? siteId = null, Guid? organizationId = null, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<WorkstationHealthEvaluationResult> list = _results.Values.ToList();
            return Task.FromResult(list);
        }

        public Task<FleetHealthSummary> GetFleetHealthSummaryAsync(Guid? siteId = null, Guid? organizationId = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new FleetHealthSummary(_results.Count, _results.Count, 0, 0, 0, 0, 0, 100.0, DateTime.UtcNow));
        }

        public Task<WorkstationHealthEvaluationResult?> GetHealthResultAsync(string pcId, CancellationToken cancellationToken = default)
        {
            _results.TryGetValue(pcId, out var res);
            return Task.FromResult(res);
        }

        public Task<WorkstationHealthEvaluationResult?> GetHealthResultByWorkstationIdAsync(Guid workstationId, CancellationToken cancellationToken = default)
        {
            var res = _results.Values.FirstOrDefault(r => r.Identity.WorkstationId == workstationId);
            return Task.FromResult(res);
        }

        public Task SaveHealthResultAsync(WorkstationHealthEvaluationResult result, CancellationToken cancellationToken = default)
        {
            SetHealth(result);
            return Task.CompletedTask;
        }
    }

    public class FakeIncidentRepository : IIncidentRepository
    {
        private readonly List<Incident> _incidents = new();

        public void Add(Incident incident) => _incidents.Add(incident);

        public Task<Incident?> GetActiveIncidentByFingerprintAsync(string fingerprint, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_incidents.FirstOrDefault(i => i.Fingerprint == fingerprint && i.LifecycleState != IncidentLifecycleState.Resolved));
        }

        public Task<IReadOnlyList<Incident>> GetActiveIncidentsAsync(Guid? organizationId = null, Guid? siteId = null, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<Incident> list = _incidents.Where(i => i.LifecycleState != IncidentLifecycleState.Resolved).ToList();
            return Task.FromResult(list);
        }

        public Task<IReadOnlyList<Incident>> GetActiveIncidentsForWorkstationAsync(string pcId, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<Incident> list = _incidents.Where(i => i.PcId == pcId && i.LifecycleState != IncidentLifecycleState.Resolved).ToList();
            return Task.FromResult(list);
        }

        public Task<Incident?> GetIncidentByIdAsync(Guid incidentId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_incidents.FirstOrDefault(i => i.Id == incidentId));
        }

        public Task<IReadOnlyList<Incident>> GetIncidentsForOrganizationAsync(Guid organizationId, IncidentLifecycleState? state = null, int skip = 0, int take = 100, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<Incident> list = _incidents.Where(i => i.OrganizationId == organizationId).Skip(skip).Take(take).ToList();
            return Task.FromResult(list);
        }

        public Task<(IReadOnlyList<Incident> Items, int TotalCount)> QueryIncidentsAsync(Guid? organizationId = null, Guid? siteId = null, Guid? workstationId = null, string? pcId = null, AlertSeverity? severity = null, IncidentLifecycleState? state = null, string? ruleName = null, bool? activeOnly = null, int skip = 0, int take = 50, CancellationToken cancellationToken = default)
        {
            var q = _incidents.AsEnumerable();
            if (severity.HasValue) q = q.Where(i => i.Severity == severity.Value);
            var list = q.ToList();
            IReadOnlyList<Incident> paged = list.Skip(skip).Take(take).ToList();
            return Task.FromResult((paged, list.Count));
        }

        public Task SaveIncidentAsync(Incident incident, CancellationToken cancellationToken = default)
        {
            _incidents.Add(incident);
            return Task.CompletedTask;
        }
    }

    public class FakeTelemetryHistoryRepository : ITelemetryHistoryRepository
    {
        private readonly List<TelemetryHistoryRecord> _records = new();

        public void AddRecord(TelemetryHistoryRecord record) => _records.Add(record);

        public Task AddAsync(TelemetryHistoryRecord entity, CancellationToken cancellationToken = default) { _records.Add(entity); return Task.CompletedTask; }

        public Task AddHeartbeatRecordAsync(HeartbeatHistoryRecord record, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Delete(TelemetryHistoryRecord entity) => _records.Remove(entity);

        public Task<IReadOnlyList<TelemetryHistoryRecord>> FindAsync(Expression<Func<TelemetryHistoryRecord, bool>> predicate, bool track = true, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<TelemetryHistoryRecord> list = _records.Where(predicate.Compile()).ToList();
            return Task.FromResult(list);
        }

        public Task<TelemetryHistoryRecord?> FirstOrDefaultAsync(Expression<Func<TelemetryHistoryRecord, bool>> predicate, bool track = true, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_records.FirstOrDefault(predicate.Compile()));
        }

        public Task<IReadOnlyList<TelemetryHistoryRecord>> GetAllAsync(bool track = true, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<TelemetryHistoryRecord> list = _records.ToList();
            return Task.FromResult(list);
        }

        public Task<TelemetryHistoryRecord?> GetByIdAsync(Guid id, bool track = true, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_records.FirstOrDefault(x => x.Id == id));
        }

        public Task<IReadOnlyList<HeartbeatHistoryRecord>> GetHeartbeatsForWorkstationAsync(Guid workstationId, DateTime? from = null, DateTime? to = null, int limit = 1000, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<HeartbeatHistoryRecord> list = new List<HeartbeatHistoryRecord>();
            return Task.FromResult(list);
        }

        public Task<IReadOnlyList<TelemetryHistoryRecord>> GetHistoryAllAsync(DateTime? from = null, DateTime? to = null, int limit = 1000, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<TelemetryHistoryRecord> list = _records.ToList();
            return Task.FromResult(list);
        }

        public Task<IReadOnlyList<TelemetryHistoryRecord>> GetHistoryForOrganizationAsync(Guid organizationId, DateTime? from = null, DateTime? to = null, int limit = 1000, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<TelemetryHistoryRecord> list = _records.Where(x => x.OrganizationId == organizationId).ToList();
            return Task.FromResult(list);
        }

        public Task<IReadOnlyList<TelemetryHistoryRecord>> GetHistoryForSiteAsync(Guid siteId, DateTime? from = null, DateTime? to = null, int limit = 1000, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<TelemetryHistoryRecord> list = _records.Where(x => x.SiteId == siteId).ToList();
            return Task.FromResult(list);
        }

        public Task<IReadOnlyList<TelemetryHistoryRecord>> GetHistoryForWorkstationAsync(Guid workstationId, DateTime? from = null, DateTime? to = null, int limit = 1000, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<TelemetryHistoryRecord> list = _records.Where(x => x.WorkstationId == workstationId).ToList();
            return Task.FromResult(list);
        }

        public void Update(TelemetryHistoryRecord entity) { }
    }

    public class FakeTelemetryAggregateRepository : ITelemetryAggregateRepository
    {
        private readonly List<TelemetryAggregateRecord> _records = new();

        public void AddRecord(TelemetryAggregateRecord record) => _records.Add(record);

        public Task AddAsync(TelemetryAggregateRecord entity, CancellationToken cancellationToken = default) { _records.Add(entity); return Task.CompletedTask; }

        public void Delete(TelemetryAggregateRecord entity) => _records.Remove(entity);

        public Task<IReadOnlyList<TelemetryAggregateRecord>> FindAsync(Expression<Func<TelemetryAggregateRecord, bool>> predicate, bool track = true, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<TelemetryAggregateRecord> list = _records.Where(predicate.Compile()).ToList();
            return Task.FromResult(list);
        }

        public Task<TelemetryAggregateRecord?> FirstOrDefaultAsync(Expression<Func<TelemetryAggregateRecord, bool>> predicate, bool track = true, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_records.FirstOrDefault(predicate.Compile()));
        }

        public Task<IReadOnlyList<TelemetryAggregateRecord>> GetAggregatesAllAsync(string granularity, DateTime? from = null, DateTime? to = null, int limit = 1000, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<TelemetryAggregateRecord> list = _records.Where(a => a.Granularity == granularity).ToList();
            return Task.FromResult(list);
        }

        public Task<IReadOnlyList<TelemetryAggregateRecord>> GetAggregatesForOrganizationAsync(Guid organizationId, string granularity, DateTime? from = null, DateTime? to = null, int limit = 1000, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<TelemetryAggregateRecord> list = _records.Where(a => a.OrganizationId == organizationId && a.Granularity == granularity).ToList();
            return Task.FromResult(list);
        }

        public Task<IReadOnlyList<TelemetryAggregateRecord>> GetAggregatesForSiteAsync(Guid siteId, string granularity, DateTime? from = null, DateTime? to = null, int limit = 1000, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<TelemetryAggregateRecord> list = _records.Where(a => a.SiteId == siteId && a.Granularity == granularity).ToList();
            return Task.FromResult(list);
        }

        public Task<IReadOnlyList<TelemetryAggregateRecord>> GetAggregatesForWorkstationAsync(Guid workstationId, string granularity, DateTime? from = null, DateTime? to = null, int limit = 1000, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<TelemetryAggregateRecord> list = _records.Where(a => a.WorkstationId == workstationId && a.Granularity == granularity).ToList();
            return Task.FromResult(list);
        }

        public Task<IReadOnlyList<TelemetryAggregateRecord>> GetAllAsync(bool track = true, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<TelemetryAggregateRecord> list = _records.ToList();
            return Task.FromResult(list);
        }

        public Task<TelemetryAggregateRecord?> GetByIdAsync(Guid id, bool track = true, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_records.FirstOrDefault(a => a.Id == id));
        }

        public Task<TelemetryAggregationCheckpoint?> GetCheckpointAsync(string granularity, CancellationToken cancellationToken = default) => Task.FromResult<TelemetryAggregationCheckpoint?>(null);

        public Task SaveAggregatesBatchAsync(IEnumerable<TelemetryAggregateRecord> aggregates, CancellationToken cancellationToken = default)
        {
            _records.AddRange(aggregates);
            return Task.CompletedTask;
        }

        public Task SaveCheckpointAsync(TelemetryAggregationCheckpoint checkpoint, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Update(TelemetryAggregateRecord entity) { }
    }

    public class FakeAuditEventRepository : IRepository<AuditEvent>
    {
        private readonly List<AuditEvent> _items = new();

        public void Add(AuditEvent item) => _items.Add(item);

        public Task AddAsync(AuditEvent entity, CancellationToken cancellationToken = default) { _items.Add(entity); return Task.CompletedTask; }

        public void Delete(AuditEvent entity) => _items.Remove(entity);

        public Task<IReadOnlyList<AuditEvent>> FindAsync(Expression<Func<AuditEvent, bool>> predicate, bool track = true, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<AuditEvent> list = _items.Where(predicate.Compile()).ToList();
            return Task.FromResult(list);
        }

        public Task<AuditEvent?> FirstOrDefaultAsync(Expression<Func<AuditEvent, bool>> predicate, bool track = true, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_items.FirstOrDefault(predicate.Compile()));
        }

        public Task<IReadOnlyList<AuditEvent>> GetAllAsync(bool track = true, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<AuditEvent> list = _items.ToList();
            return Task.FromResult(list);
        }

        public Task<AuditEvent?> GetByIdAsync(Guid id, bool track = true, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_items.FirstOrDefault(a => a.Id == id));
        }

        public void Update(AuditEvent entity) { }
    }

    #endregion
}
