using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using Sayra.Backend.Application.Abstractions.Caching;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Telemetry;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain.Enums;

namespace Sayra.Backend.UnitTests.Telemetry
{
    public class TelemetryIngestionTests
    {
        private readonly Mock<ISecurityEventService> _securityEventServiceMock = new();
        private readonly Mock<IRedisService> _redisServiceMock = new();
        private readonly TelemetryIdempotencyService _idempotencyService;
        private readonly TelemetryIngestionService _ingestionService;

        public TelemetryIngestionTests()
        {
            _idempotencyService = new TelemetryIdempotencyService(_redisServiceMock.Object);
            _ingestionService = new TelemetryIngestionService(
                _securityEventServiceMock.Object,
                _idempotencyService,
                NullLogger<TelemetryIngestionService>.Instance);
        }

        #region 1. Contract Tests

        [Fact]
        public async Task IngestHeartbeat_ValidHeartbeat_ShouldBeAccepted()
        {
            // Arrange
            var context = new TelemetryConnectionContext("CONN-001", "PC-001");
            var heartbeat = new HeartbeatMessage
            {
                PcId = "PC-001",
                Timestamp = DateTime.UtcNow
            };

            // Act
            var result = await _ingestionService.IngestHeartbeatAsync(context, heartbeat, CancellationToken.None);

            // Assert
            Assert.True(result.IsAccepted);
            Assert.Equal(TelemetryIngestionStatus.Accepted, result.Status);
            Assert.Equal(TelemetryMessageType.Heartbeat, result.MessageType);
            Assert.NotNull(result.HeartbeatSignal);
            Assert.Equal("PC-001", result.HeartbeatSignal!.Identity.PcId);
        }

        [Fact]
        public async Task IngestTelemetrySnapshot_ValidModelWithGameInfo_ShouldBeAccepted()
        {
            // Arrange
            var context = new TelemetryConnectionContext("CONN-001", "PC-001");
            var model = new TelemetryModel
            {
                Cpu = 45.2,
                Ram = 4096.0,
                Uptime = 86400.0,
                Timestamp = DateTime.UtcNow,
                RunningGameName = "Counter-Strike 2",
                RunningGamePid = 1234,
                RunningGameCpu = 12.5,
                RunningGameRam = 2048.0,
                RunningGameDuration = 3600.0,
                TotalLaunches = 10,
                TotalCrashes = 1,
                TotalRestarts = 2
            };

            // Act
            var result = await _ingestionService.IngestTelemetrySnapshotAsync(context, model, CancellationToken.None);

            // Assert
            Assert.True(result.IsAccepted);
            Assert.Equal(TelemetryIngestionStatus.Accepted, result.Status);
            Assert.Equal(TelemetryMessageType.Telemetry, result.MessageType);
            Assert.NotNull(result.Snapshot);
            Assert.Equal("Counter-Strike 2", result.Snapshot!.RunningGameName);
            Assert.Equal(1234, result.Snapshot.RunningGamePid);
        }

        [Fact]
        public async Task IngestOperationalEvent_ValidEventEnvelope_ShouldBeAccepted()
        {
            // Arrange
            var context = new TelemetryConnectionContext("CONN-001", "PC-001");
            string eventId = Guid.NewGuid().ToString();
            string correlationId = Guid.NewGuid().ToString();
            string sessionId = Guid.NewGuid().ToString();

            var eventDto = new ClientEventEnvelopeDto
            {
                EventId = eventId,
                EventType = ClientEventType.ApplicationStarted,
                ClientId = "PC-001",
                WorkstationId = "PC-001",
                SessionId = sessionId,
                CorrelationId = correlationId,
                OccurredAt = DateTime.UtcNow,
                Payload = "{\"game\":\"CS2\"}"
            };

            // Act
            var result = await _ingestionService.IngestOperationalEventAsync(context, eventDto, CancellationToken.None);

            // Assert
            Assert.True(result.IsAccepted);
            Assert.Equal(TelemetryIngestionStatus.Accepted, result.Status);
            Assert.Equal(TelemetryMessageType.OperationalEvent, result.MessageType);
            Assert.NotNull(result.EventSignal);
            Assert.Equal(eventId, result.EventSignal!.EventId);
            Assert.Equal(ClientEventType.ApplicationStarted, result.EventSignal.EventType);
            Assert.Equal(correlationId, result.EventSignal.CorrelationId);
            Assert.Equal(sessionId, result.EventSignal.SessionId);
        }

        #endregion

        #region 2. Validation Tests

        [Theory]
        [InlineData(-1.0, 1024, 100, TelemetryRejectionReason.InvalidCpuRange)]
        [InlineData(101.0, 1024, 100, TelemetryRejectionReason.InvalidCpuRange)]
        [InlineData(50.0, -10.0, 100, TelemetryRejectionReason.InvalidRamRange)]
        [InlineData(50.0, 1024, -5, TelemetryRejectionReason.InvalidUptimeRange)]
        public async Task IngestTelemetry_InvalidMetrics_ShouldBeRejected(double cpu, double ram, double uptime, TelemetryRejectionReason expectedReason)
        {
            // Arrange
            var context = new TelemetryConnectionContext("CONN-001", "PC-001");
            var model = new TelemetryModel
            {
                Cpu = cpu,
                Ram = ram,
                Uptime = uptime,
                Timestamp = DateTime.UtcNow
            };

            // Act
            var result = await _ingestionService.IngestTelemetrySnapshotAsync(context, model, CancellationToken.None);

            // Assert
            Assert.False(result.IsAccepted);
            Assert.Equal(TelemetryIngestionStatus.Rejected, result.Status);
            Assert.Equal(expectedReason, result.RejectionReason);
        }

        [Fact]
        public async Task IngestTelemetry_RunningGameNameTooLong_ShouldBeRejected()
        {
            // Arrange
            var context = new TelemetryConnectionContext("CONN-001", "PC-001");
            var model = new TelemetryModel
            {
                Cpu = 10,
                Ram = 1024,
                Uptime = 100,
                Timestamp = DateTime.UtcNow,
                RunningGameName = new string('A', 257)
            };

            // Act
            var result = await _ingestionService.IngestTelemetrySnapshotAsync(context, model, CancellationToken.None);

            // Assert
            Assert.False(result.IsAccepted);
            Assert.Equal(TelemetryRejectionReason.GameNameExceedsMaxLength, result.RejectionReason);
        }

        [Fact]
        public async Task IngestTelemetry_FutureTimestamp_ShouldBeRejected()
        {
            // Arrange
            var context = new TelemetryConnectionContext("CONN-001", "PC-001");
            var model = new TelemetryModel
            {
                Cpu = 10,
                Ram = 1024,
                Uptime = 100,
                Timestamp = DateTime.UtcNow.AddMinutes(10) // > 5 minutes in future
            };

            // Act
            var result = await _ingestionService.IngestTelemetrySnapshotAsync(context, model, CancellationToken.None);

            // Assert
            Assert.False(result.IsAccepted);
            Assert.Equal(TelemetryRejectionReason.TimestampInFuture, result.RejectionReason);
        }

        [Fact]
        public async Task IngestTelemetry_ExcessivelyOldTimestamp_ShouldBeRejected()
        {
            // Arrange
            var context = new TelemetryConnectionContext("CONN-001", "PC-001");
            var model = new TelemetryModel
            {
                Cpu = 10,
                Ram = 1024,
                Uptime = 100,
                Timestamp = DateTime.UtcNow.AddHours(-25) // > 24 hours old
            };

            // Act
            var result = await _ingestionService.IngestTelemetrySnapshotAsync(context, model, CancellationToken.None);

            // Assert
            Assert.False(result.IsAccepted);
            Assert.Equal(TelemetryRejectionReason.TimestampExcessivelyOld, result.RejectionReason);
        }

        [Fact]
        public async Task IngestOperationalEvent_MissingEventIdOrType_ShouldBeRejected()
        {
            // Arrange
            var context = new TelemetryConnectionContext("CONN-001", "PC-001");
            var invalidDto = new ClientEventEnvelopeDto
            {
                EventId = "", // Missing
                EventType = ClientEventType.ClientStarted,
                ClientId = "PC-001",
                WorkstationId = "PC-001"
            };

            // Act
            var result = await _ingestionService.IngestOperationalEventAsync(context, invalidDto, CancellationToken.None);

            // Assert
            Assert.False(result.IsAccepted);
            Assert.Equal(TelemetryRejectionReason.MissingEventId, result.RejectionReason);
        }

        #endregion

        #region 3. Identity Binding Tests

        [Fact]
        public async Task IngestOperationalEvent_MismatchedClientId_ShouldBeRejectedAndAuditLogSecurityEvent()
        {
            // Arrange
            var context = new TelemetryConnectionContext("CONN-001", "PC-001");
            var spoofedDto = new ClientEventEnvelopeDto
            {
                EventId = Guid.NewGuid().ToString(),
                EventType = ClientEventType.SecurityEvent,
                ClientId = "PC-999", // Spoofed ClientId!
                WorkstationId = "PC-001",
                OccurredAt = DateTime.UtcNow
            };

            // Act
            var result = await _ingestionService.IngestOperationalEventAsync(context, spoofedDto, CancellationToken.None);

            // Assert
            Assert.False(result.IsAccepted);
            Assert.Equal(TelemetryIngestionStatus.IdentityMismatch, result.Status);
            Assert.Equal(TelemetryRejectionReason.IdentityMismatch, result.RejectionReason);

            _securityEventServiceMock.Verify(s => s.RecordSecurityEventAsync(
                "TELEMETRY_IDENTITY_MISMATCH",
                It.IsAny<Guid?>(),
                "Workstation",
                "PC-001",
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
        public async Task IngestHeartbeat_MismatchedPcId_ShouldBeRejected()
        {
            // Arrange
            var context = new TelemetryConnectionContext("CONN-001", "PC-001");
            var spoofedHeartbeat = new HeartbeatMessage
            {
                PcId = "PC-999", // Spoofed PC-ID!
                Timestamp = DateTime.UtcNow
            };

            // Act
            var result = await _ingestionService.IngestHeartbeatAsync(context, spoofedHeartbeat, CancellationToken.None);

            // Assert
            Assert.False(result.IsAccepted);
            Assert.Equal(TelemetryIngestionStatus.IdentityMismatch, result.Status);
            Assert.Equal(TelemetryRejectionReason.IdentityMismatch, result.RejectionReason);
        }

        #endregion

        #region 4. Ordering & Idempotency Tests

        [Fact]
        public async Task IngestOperationalEvent_DuplicateEventId_ShouldReturnDuplicateStatus()
        {
            // Arrange
            var context = new TelemetryConnectionContext("CONN-001", "PC-001");
            string eventId = Guid.NewGuid().ToString();

            var eventDto = new ClientEventEnvelopeDto
            {
                EventId = eventId,
                EventType = ClientEventType.ClientStarted,
                ClientId = "PC-001",
                WorkstationId = "PC-001",
                OccurredAt = DateTime.UtcNow
            };

            _redisServiceMock.Setup(r => r.GetStringAsync($"v1:event:dedup:{eventId}", It.IsAny<CancellationToken>()))
                .ReturnsAsync("PROCESSED");

            // Act
            var result = await _ingestionService.IngestOperationalEventAsync(context, eventDto, CancellationToken.None);

            // Assert
            Assert.True(result.IsAccepted);
            Assert.Equal(TelemetryIngestionStatus.Duplicate, result.Status);
            Assert.Equal(TelemetryRejectionReason.DuplicateEvent, result.RejectionReason);
        }

        [Fact]
        public async Task IngestTelemetrySnapshot_OlderTimestampThanLatest_ShouldReturnStaleStatus()
        {
            // Arrange
            var context = new TelemetryConnectionContext("CONN-001", "PC-001");
            var now = DateTime.UtcNow;

            var olderModel = new TelemetryModel
            {
                Cpu = 10,
                Ram = 1024,
                Uptime = 100,
                Timestamp = now.AddMinutes(-5)
            };

            string tsKey = "v1:telemetry:PC-001:latest_ts";
            _redisServiceMock.Setup(r => r.GetStringAsync(tsKey, It.IsAny<CancellationToken>()))
                .ReturnsAsync(now.Ticks.ToString()); // Latest in Redis is 'now'

            // Act
            var result = await _ingestionService.IngestTelemetrySnapshotAsync(context, olderModel, CancellationToken.None);

            // Assert
            Assert.False(result.IsAccepted);
            Assert.Equal(TelemetryIngestionStatus.Stale, result.Status);
            Assert.Equal(TelemetryRejectionReason.StaleTelemetrySnapshot, result.RejectionReason);
        }

        #endregion

        #region 5. Classification & Error Handling Tests

        [Fact]
        public async Task MessageClassification_PreservesSemanticType()
        {
            // Arrange
            var context = new TelemetryConnectionContext("CONN-001", "PC-001");

            var hbResult = await _ingestionService.IngestHeartbeatAsync(context, new HeartbeatMessage { PcId = "PC-001", Timestamp = DateTime.UtcNow }, CancellationToken.None);
            var telemResult = await _ingestionService.IngestTelemetrySnapshotAsync(context, new TelemetryModel { Cpu = 10, Ram = 1024, Uptime = 100, Timestamp = DateTime.UtcNow }, CancellationToken.None);
            var evtResult = await _ingestionService.IngestOperationalEventAsync(context, new ClientEventEnvelopeDto { EventId = "E1", EventType = "TEST", ClientId = "PC-001", WorkstationId = "PC-001", OccurredAt = DateTime.UtcNow }, CancellationToken.None);

            // Assert
            Assert.Equal(TelemetryMessageType.Heartbeat, hbResult.MessageType);
            Assert.Equal(TelemetryMessageType.Telemetry, telemResult.MessageType);
            Assert.Equal(TelemetryMessageType.OperationalEvent, evtResult.MessageType);
        }

        [Fact]
        public async Task NullPayload_ShouldReturnControlledRejection()
        {
            // Arrange
            var context = new TelemetryConnectionContext("CONN-001", "PC-001");

            // Act
            var hbResult = await _ingestionService.IngestHeartbeatAsync(context, null!, CancellationToken.None);
            var telemResult = await _ingestionService.IngestTelemetrySnapshotAsync(context, null!, CancellationToken.None);
            var evtResult = await _ingestionService.IngestOperationalEventAsync(context, null!, CancellationToken.None);

            // Assert
            Assert.False(hbResult.IsAccepted);
            Assert.False(telemResult.IsAccepted);
            Assert.False(evtResult.IsAccepted);

            Assert.Equal(TelemetryRejectionReason.PayloadNull, hbResult.RejectionReason);
            Assert.Equal(TelemetryRejectionReason.PayloadNull, telemResult.RejectionReason);
            Assert.Equal(TelemetryRejectionReason.PayloadNull, evtResult.RejectionReason);
        }

        #endregion
    }
}
