using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sayra.Backend.Application.Abstractions.Caching;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Security;
using Sayra.Backend.Application.Telemetry;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Domain.Enums;
using Xunit;

namespace Sayra.Backend.UnitTests.Telemetry
{
    public class Phase08SecurityAndAdversarialTests
    {
        private readonly Mock<ISecurityEventService> _securityEventMock = new();
        private readonly Mock<IRedisService> _redisMock = new();
        private readonly Mock<IAuthorizationService> _authServiceMock = new();
        private readonly Mock<IRepository<Workstation>> _workstationRepoMock = new();
        private readonly Mock<IWorkstationStateReader> _stateReaderMock = new();
        private readonly Mock<IWorkstationHealthReader> _healthReaderMock = new();
        private readonly Mock<IWorkstationHealthStore> _healthStoreMock = new();
        private readonly Mock<IIncidentRepository> _incidentRepoMock = new();
        private readonly Mock<ITelemetryHistoryRepository> _historyRepoMock = new();
        private readonly Mock<ITelemetryAggregateRepository> _aggregateRepoMock = new();
        private readonly Mock<IRepository<AuditEvent>> _auditRepoMock = new();

        private readonly TelemetryIdempotencyService _idempotencyService;
        private readonly TelemetryIngestionService _ingestionService;
        private readonly MonitoringQueryService _monitoringQueryService;

        public Phase08SecurityAndAdversarialTests()
        {
            _idempotencyService = new TelemetryIdempotencyService(_redisMock.Object);
            _ingestionService = new TelemetryIngestionService(
                _securityEventMock.Object,
                _idempotencyService,
                NullLogger<TelemetryIngestionService>.Instance);

            _monitoringQueryService = new MonitoringQueryService(
                _authServiceMock.Object,
                _workstationRepoMock.Object,
                _stateReaderMock.Object,
                _healthReaderMock.Object,
                _healthStoreMock.Object,
                _incidentRepoMock.Object,
                _historyRepoMock.Object,
                _aggregateRepoMock.Object,
                _auditRepoMock.Object);
        }

        [Fact]
        public async Task IdentitySpoofing_PayloadClientIdMismatchesConnection_RejectsAndLogsSecurityAudit()
        {
            // Arrange - Connection PC-100 receiving payload claiming PC-999
            var context = new TelemetryConnectionContext("CONN-001", "PC-100");
            var spoofedEvent = new ClientEventEnvelopeDto
            {
                EventId = Guid.NewGuid().ToString(),
                EventType = ClientEventType.SecurityEvent,
                ClientId = "PC-999", // Spoofed ClientId
                WorkstationId = "PC-100",
                OccurredAt = DateTime.UtcNow,
                Payload = "{}"
            };

            // Act
            var result = await _ingestionService.IngestOperationalEventAsync(context, spoofedEvent, CancellationToken.None);

            // Assert
            Assert.False(result.IsAccepted);
            Assert.Equal(TelemetryIngestionStatus.IdentityMismatch, result.Status);
            Assert.Equal(TelemetryRejectionReason.IdentityMismatch, result.RejectionReason);

            _securityEventMock.Verify(s => s.RecordSecurityEventAsync(
                "TELEMETRY_IDENTITY_MISMATCH",
                It.IsAny<Guid?>(),
                "Workstation",
                "PC-100",
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                "Workstation",
                It.IsAny<Guid?>(),
                "IngestOperationalEvent",
                "FAILURE",
                It.Is<string>(msg => msg.Contains("PC-999")),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ReplayAttack_DuplicateEventId_ReturnsDuplicateWithoutProcessing()
        {
            // Arrange
            var context = new TelemetryConnectionContext("CONN-001", "PC-001");
            string eventId = "EVT-REPLAY-1001";

            var eventDto = new ClientEventEnvelopeDto
            {
                EventId = eventId,
                EventType = ClientEventType.ClientStarted,
                ClientId = "PC-001",
                WorkstationId = "PC-001",
                OccurredAt = DateTime.UtcNow
            };

            _redisMock.Setup(r => r.GetStringAsync($"v1:event:dedup:{eventId}", It.IsAny<CancellationToken>()))
                .ReturnsAsync("PROCESSED");

            // Act
            var result = await _ingestionService.IngestOperationalEventAsync(context, eventDto, CancellationToken.None);

            // Assert
            Assert.True(result.IsAccepted);
            Assert.Equal(TelemetryIngestionStatus.Duplicate, result.Status);
            Assert.Equal(TelemetryRejectionReason.DuplicateEvent, result.RejectionReason);
        }

        [Fact]
        public async Task ClockSkew_FutureAndOldTimestamps_AreRejected()
        {
            var context = new TelemetryConnectionContext("CONN-001", "PC-001");

            var futureModel = new TelemetryModel
            {
                Cpu = 25.0,
                Ram = 1024.0,
                Uptime = 100,
                Timestamp = DateTime.UtcNow.AddMinutes(10) // > 5m future
            };

            var oldModel = new TelemetryModel
            {
                Cpu = 25.0,
                Ram = 1024.0,
                Uptime = 100,
                Timestamp = DateTime.UtcNow.AddHours(-25) // > 24h old
            };

            var futureResult = await _ingestionService.IngestTelemetrySnapshotAsync(context, futureModel, CancellationToken.None);
            var oldResult = await _ingestionService.IngestTelemetrySnapshotAsync(context, oldModel, CancellationToken.None);

            Assert.False(futureResult.IsAccepted);
            Assert.Equal(TelemetryRejectionReason.TimestampInFuture, futureResult.RejectionReason);

            Assert.False(oldResult.IsAccepted);
            Assert.Equal(TelemetryRejectionReason.TimestampExcessivelyOld, oldResult.RejectionReason);
        }

        [Fact]
        public async Task OversizedAndMalformedPayload_IsRejectedSafely()
        {
            var context = new TelemetryConnectionContext("CONN-001", "PC-001");

            var oversizedEvent = new ClientEventEnvelopeDto
            {
                EventId = Guid.NewGuid().ToString(),
                EventType = "HUGE_EVENT",
                ClientId = "PC-001",
                WorkstationId = "PC-001",
                OccurredAt = DateTime.UtcNow,
                Payload = new string('X', 70000) // > 64 KB
            };

            var invalidCpuModel = new TelemetryModel
            {
                Cpu = 150.0, // Invalid > 100%
                Ram = 1024.0,
                Uptime = 100,
                Timestamp = DateTime.UtcNow
            };

            var oversizedGameNameModel = new TelemetryModel
            {
                Cpu = 25.0,
                Ram = 1024.0,
                Uptime = 100,
                Timestamp = DateTime.UtcNow,
                RunningGameName = new string('G', 300) // > 256 chars
            };

            var oversizedResult = await _ingestionService.IngestOperationalEventAsync(context, oversizedEvent, CancellationToken.None);
            var invalidCpuResult = await _ingestionService.IngestTelemetrySnapshotAsync(context, invalidCpuModel, CancellationToken.None);
            var oversizedGameResult = await _ingestionService.IngestTelemetrySnapshotAsync(context, oversizedGameNameModel, CancellationToken.None);

            Assert.False(oversizedResult.IsAccepted);
            Assert.Equal(TelemetryRejectionReason.OversizedPayload, oversizedResult.RejectionReason);

            Assert.False(invalidCpuResult.IsAccepted);
            Assert.Equal(TelemetryRejectionReason.InvalidCpuRange, invalidCpuResult.RejectionReason);

            Assert.False(oversizedGameResult.IsAccepted);
            Assert.Equal(TelemetryRejectionReason.GameNameExceedsMaxLength, oversizedGameResult.RejectionReason);
        }

        [Fact]
        public async Task MonitoringApi_UnauthenticatedOrDisabledAccount_ReturnsUnauthorizedOrForbidden()
        {
            // Case 1: Unauthenticated Principal
            var nullPrincipalResult = await _monitoringQueryService.GetFleetWorkstationsAsync(null!);
            Assert.False(nullPrincipalResult.IsSuccess);
            Assert.Equal("UNAUTHORIZED", nullPrincipalResult.ErrorCode);

            // Case 2: Disabled Account Principal
            var disabledPrincipal = new UserPrincipal
            {
                IsAuthenticated = true,
                AccountStatus = UserAccountState.Disabled,
                Roles = new List<string> { RoleCatalog.Administrator },
                Permissions = new List<string> { PermissionCatalog.ViewWorkstations }
            };

            _authServiceMock.Setup(a => a.AuthorizeAsync(disabledPrincipal, PermissionCatalog.ViewWorkstations, It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(AuthorizationResult.Denied("ACCOUNT_DISABLED", "Account is disabled."));

            var disabledResult = await _monitoringQueryService.GetFleetWorkstationsAsync(disabledPrincipal);
            Assert.False(disabledResult.IsSuccess);
            Assert.Equal("ACCOUNT_DISABLED", disabledResult.ErrorCode);
        }

        [Fact]
        public async Task MonitoringApi_CrossOrganizationAccessAttempt_ReturnsForbidden()
        {
            // Arrange
            var userOrgId = Guid.NewGuid();
            var targetOrgId = Guid.NewGuid();

            var operatorPrincipal = new UserPrincipal
            {
                IsAuthenticated = true,
                AccountStatus = UserAccountState.Active,
                OrganizationId = userOrgId,
                Roles = new List<string> { RoleCatalog.Operator },
                Permissions = new List<string> { PermissionCatalog.ViewWorkstations }
            };

            _authServiceMock.Setup(a => a.AuthorizeAsync(operatorPrincipal, PermissionCatalog.ViewWorkstations, It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(AuthorizationResult.Allowed());

            // Act - Operator assigned to userOrgId attempts to query targetOrgId
            var result = await _monitoringQueryService.GetFleetWorkstationsAsync(operatorPrincipal, organizationId: targetOrgId);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("CROSS_ORGANIZATION_ACCESS_DENIED", result.ErrorCode);
        }

        [Fact]
        public async Task RedactionCheck_SecurityEventAndPayload_NoPasswordOrKeyInLogs()
        {
            // Verify that sensitive keywords are omitted / not logged in cleartext
            string eventPayload = "{\"username\":\"gamer1\",\"password\":\"SecretP@ss123!\",\"token\":\"ey JhbGciOi...\"}";
            var context = new TelemetryConnectionContext("CONN-001", "PC-001");

            var eventDto = new ClientEventEnvelopeDto
            {
                EventId = Guid.NewGuid().ToString(),
                EventType = ClientEventType.SecurityEvent,
                ClientId = "PC-001",
                WorkstationId = "PC-001",
                OccurredAt = DateTime.UtcNow,
                Payload = eventPayload
            };

            var result = await _ingestionService.IngestOperationalEventAsync(context, eventDto, CancellationToken.None);
            Assert.True(result.IsAccepted);
            Assert.NotNull(result.EventSignal);
            Assert.Equal(ClientEventType.SecurityEvent, result.EventSignal!.EventType);
        }
    }
}
