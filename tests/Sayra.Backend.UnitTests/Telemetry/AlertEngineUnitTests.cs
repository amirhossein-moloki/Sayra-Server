using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Telemetry;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Domain.Enums;
using Sayra.Backend.Domain.Telemetry;
using Sayra.Backend.Domain.ValueObjects;
using Sayra.Backend.Infrastructure.Telemetry;
using Xunit;

namespace Sayra.Backend.UnitTests.Telemetry
{
    public class AlertEngineUnitTests
    {
        private readonly Mock<IIncidentRepository> _incidentRepositoryMock;
        private readonly Mock<IAlertMetrics> _metricsMock;
        private readonly Mock<IAlertNotificationDispatcher> _dispatcherMock;
        private readonly IOptions<AlertingOptions> _options;
        private readonly AlertEvaluationEngine _engine;

        public AlertEngineUnitTests()
        {
            _incidentRepositoryMock = new Mock<IIncidentRepository>();
            _metricsMock = new Mock<IAlertMetrics>();
            _dispatcherMock = new Mock<IAlertNotificationDispatcher>();

            var alertingOptions = new AlertingOptions
            {
                IsEnabled = true,
                Rules = AlertingOptions.GetDefaultRules()
            };
            _options = Options.Create(alertingOptions);

            _engine = new AlertEvaluationEngine(
                _incidentRepositoryMock.Object,
                _options,
                _metricsMock.Object,
                _dispatcherMock.Object,
                NullLogger<AlertEvaluationEngine>.Instance);
        }

        [Fact]
        public void CalculateFingerprint_ProducesDeterministicStableHash()
        {
            var orgId = Guid.NewGuid();
            var siteId = Guid.NewGuid();
            string pcId = "PC-LAB-01";
            string ruleCode = "CPU_SUSTAINED_HIGH";

            string fp1 = _engine.CalculateFingerprint(orgId, siteId, pcId, ruleCode, "CPU");
            string fp2 = _engine.CalculateFingerprint(orgId, siteId, pcId, ruleCode, "CPU");

            Assert.NotNull(fp1);
            Assert.Equal(64, fp1.Length);
            Assert.Equal(fp1, fp2);
        }

        [Fact]
        public async Task EvaluateHealthResultAsync_NewDegradedHealth_CreatesFiringIncident()
        {
            var identity = new WorkstationIdentity("PC-LAB-01", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            var now = DateTime.UtcNow;

            var reasons = new List<WorkstationHealthReason>
            {
                new WorkstationHealthReason("CPU_SUSTAINED_HIGH", WorkstationHealthState.Degraded, "CPU", 92.0, 80.0, "CPU high for 5 mins", now)
            };

            var healthResult = new WorkstationHealthEvaluationResult(
                identity,
                WorkstationHealthState.Degraded,
                40.0,
                reasons,
                now,
                "v1.0");

            _incidentRepositoryMock
                .Setup(r => r.GetActiveIncidentsForWorkstationAsync(identity.PcId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Incident>());

            Incident? savedIncident = null;
            _incidentRepositoryMock
                .Setup(r => r.SaveIncidentAsync(It.IsAny<Incident>(), It.IsAny<CancellationToken>()))
                .Callback<Incident, CancellationToken>((inc, ct) => savedIncident = inc)
                .Returns(Task.CompletedTask);

            var incidents = await _engine.EvaluateHealthResultAsync(healthResult);

            Assert.Single(incidents);
            Assert.NotNull(savedIncident);
            Assert.Equal(IncidentLifecycleState.Firing, savedIncident!.LifecycleState);
            Assert.Equal("CPU_SUSTAINED_HIGH", savedIncident.RuleCode);
            Assert.Equal(identity.PcId, savedIncident.PcId);
            Assert.Equal(identity.OrganizationId, savedIncident.OrganizationId);
            Assert.Equal(identity.SiteId, savedIncident.SiteId);
            Assert.Equal(AlertSeverity.Error, savedIncident.Severity);

            _dispatcherMock.Verify(d => d.DispatchNotificationAsync(It.IsAny<Incident>(), "FIRING", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task EvaluateHealthResultAsync_ExistingActiveIncident_DeduplicatesAndIncrementsObservationCount()
        {
            var identity = new WorkstationIdentity("PC-LAB-01", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            var now = DateTime.UtcNow;

            string ruleCode = "MEMORY_SUSTAINED_HIGH";
            string fingerprint = _engine.CalculateFingerprint(identity.OrganizationId!.Value, identity.SiteId, identity.PcId, ruleCode, "RAM");

            var existingIncident = Incident.CreateFiring(
                fingerprint,
                ruleCode,
                identity.OrganizationId!.Value,
                identity.SiteId,
                identity.WorkstationId,
                identity.PcId,
                AlertSeverity.Warning,
                ruleCode,
                "RAM",
                "Memory High on PC-LAB-01",
                "RAM 90%",
                "{}",
                now.AddMinutes(-5));

            _incidentRepositoryMock
                .Setup(r => r.GetActiveIncidentsForWorkstationAsync(identity.PcId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Incident> { existingIncident });

            var reasons = new List<WorkstationHealthReason>
            {
                new WorkstationHealthReason(ruleCode, WorkstationHealthState.Warning, "RAM", 91.0, 85.0, "RAM high 91%", now)
            };

            var healthResult = new WorkstationHealthEvaluationResult(
                identity,
                WorkstationHealthState.Warning,
                60.0,
                reasons,
                now,
                "v1.0");

            var incidents = await _engine.EvaluateHealthResultAsync(healthResult);

            Assert.Single(incidents);
            Assert.Equal(2, existingIncident.ObservationCount);
            Assert.Equal(now, existingIncident.LastObservedAtUtc);

            _metricsMock.Verify(m => m.RecordIncidentDeduplicated(ruleCode), Times.Once);
        }

        [Fact]
        public async Task EvaluateHealthResultAsync_ClearedCondition_ResolvesActiveIncident()
        {
            var identity = new WorkstationIdentity("PC-LAB-01", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            var now = DateTime.UtcNow;

            string ruleCode = "CPU_SUSTAINED_HIGH";
            string fingerprint = _engine.CalculateFingerprint(identity.OrganizationId!.Value, identity.SiteId, identity.PcId, ruleCode, "CPU");

            var existingIncident = Incident.CreateFiring(
                fingerprint,
                ruleCode,
                identity.OrganizationId!.Value,
                identity.SiteId,
                identity.WorkstationId,
                identity.PcId,
                AlertSeverity.Error,
                ruleCode,
                "CPU",
                "CPU High on PC-LAB-01",
                "CPU 95%",
                "{}",
                now.AddMinutes(-10));

            _incidentRepositoryMock
                .Setup(r => r.GetActiveIncidentsForWorkstationAsync(identity.PcId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Incident> { existingIncident });

            var healthResult = WorkstationHealthEvaluationResult.CreateHealthy(identity, now, "v1.0");

            var incidents = await _engine.EvaluateHealthResultAsync(healthResult);

            Assert.Single(incidents);
            Assert.Equal(IncidentLifecycleState.Resolved, existingIncident.LifecycleState);
            Assert.Equal(now, existingIncident.ResolvedAtUtc);
            Assert.NotNull(existingIncident.RecoveryEvidence);

            _metricsMock.Verify(m => m.RecordIncidentResolved(ruleCode), Times.Once);
            _dispatcherMock.Verify(d => d.DispatchNotificationAsync(existingIncident, "RESOLVED", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task EvaluateHealthResultAsync_MinSustainedDuration_TriggersBeforeFiring()
        {
            var identity = new WorkstationIdentity("PC-LAB-01", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            var t0 = DateTime.UtcNow.AddMinutes(-2);

            var optionsWithSustained = new AlertingOptions
            {
                IsEnabled = true,
                Rules = new List<AlertRule>
                {
                    new AlertRule("CPU_SUSTAINED_HIGH", "CPU High", "CPU High", AlertSeverity.Warning, 300, true, 300, "HealthEvaluator", 80.0)
                }
            };

            var engine = new AlertEvaluationEngine(
                _incidentRepositoryMock.Object,
                Options.Create(optionsWithSustained),
                _metricsMock.Object,
                _dispatcherMock.Object,
                NullLogger<AlertEvaluationEngine>.Instance);

            _incidentRepositoryMock
                .Setup(r => r.GetActiveIncidentsForWorkstationAsync(identity.PcId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Incident>());

            Incident? createdIncident = null;
            _incidentRepositoryMock
                .Setup(r => r.SaveIncidentAsync(It.IsAny<Incident>(), It.IsAny<CancellationToken>()))
                .Callback<Incident, CancellationToken>((inc, ct) => createdIncident = inc)
                .Returns(Task.CompletedTask);

            var reasons = new List<WorkstationHealthReason>
            {
                new WorkstationHealthReason("CPU_SUSTAINED_HIGH", WorkstationHealthState.Warning, "CPU", 85.0, 80.0, "CPU 85%", t0)
            };

            var healthResult = new WorkstationHealthEvaluationResult(identity, WorkstationHealthState.Warning, 70.0, reasons, t0, "v1.0");

            await engine.EvaluateHealthResultAsync(healthResult);

            Assert.NotNull(createdIncident);
            Assert.Equal(IncidentLifecycleState.Triggered, createdIncident!.LifecycleState);
        }

        [Fact]
        public async Task EvaluateOperationalEvent_DirectEvent_CreatesOrUpdatesIncident()
        {
            var identity = new WorkstationIdentity("PC-LAB-02", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            var now = DateTime.UtcNow;

            var eventSignal = new OperationalEventSignal(
                "EVT-100",
                "UPDATE_FAILED",
                identity,
                "SESS-123",
                "CORR-456",
                now,
                "{\"packageId\":\"PKG-888\",\"error\":\"VerificationFailed\"}",
                now,
                now);

            _incidentRepositoryMock
                .Setup(r => r.GetActiveIncidentByFingerprintAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Incident?)null);

            var incident = await _engine.EvaluateOperationalEventAsync(eventSignal);

            Assert.NotNull(incident);
            Assert.Equal("UPDATE_FAILED", incident!.RuleCode);
            Assert.Equal("PC-LAB-02", incident.PcId);
            Assert.Equal(IncidentLifecycleState.Firing, incident.LifecycleState);

            _dispatcherMock.Verify(d => d.DispatchNotificationAsync(incident, "FIRING", It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
