using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Xunit;
using Sayra.Backend.Application.Abstractions.Caching;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Events;
using Sayra.Backend.Application.Telemetry;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;

namespace Sayra.Backend.UnitTests
{
    public class TelemetryAndEventUnitTests
    {
        private readonly Mock<IRepository<TelemetryMetric>> _telemetryRepoMock = new();
        private readonly Mock<IRepository<AuditEvent>> _auditRepoMock = new();
        private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
        private readonly Mock<IRedisService> _redisServiceMock = new();

        [Fact]
        public async Task IngestTelemetry_ValidPayload_ShouldSucceed()
        {
            // Arrange
            var handler = new IngestTelemetryCommandHandler(_telemetryRepoMock.Object, _unitOfWorkMock.Object, _redisServiceMock.Object);
            var model = new TelemetryModel
            {
                Cpu = 45.5,
                Ram = 1024,
                Uptime = 3600,
                Timestamp = DateTime.UtcNow
            };
            var command = new IngestTelemetryCommand(Guid.NewGuid(), "PC-001", model);

            // Act
            var result = await handler.HandleAsync(command, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.True(result.Value);
            _telemetryRepoMock.Verify(r => r.AddAsync(It.IsAny<TelemetryMetric>(), It.IsAny<CancellationToken>()), Times.Once);
            _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task IngestTelemetry_InvalidCpu_ShouldFailValidation()
        {
            // Arrange
            var handler = new IngestTelemetryCommandHandler(_telemetryRepoMock.Object, _unitOfWorkMock.Object, _redisServiceMock.Object);
            var model = new TelemetryModel
            {
                Cpu = 150.0, // Invalid CPU percentage (> 100%)
                Ram = 1024,
                Uptime = 3600
            };
            var command = new IngestTelemetryCommand(Guid.NewGuid(), "PC-001", model);

            // Act
            var result = await handler.HandleAsync(command, CancellationToken.None);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("CPU usage must be between 0% and 100%.", result.ErrorCode);
            _telemetryRepoMock.Verify(r => r.AddAsync(It.IsAny<TelemetryMetric>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task IngestClientEvent_ValidEvent_ShouldPersistAuditEvent()
        {
            // Arrange
            var handler = new IngestClientEventCommandHandler(_auditRepoMock.Object, _unitOfWorkMock.Object, _redisServiceMock.Object);
            var evtDto = new ClientEventEnvelopeDto
            {
                EventId = Guid.NewGuid().ToString(),
                EventType = ClientEventType.ClientStarted,
                ClientId = "PC-100",
                WorkstationId = "PC-100",
                OccurredAt = DateTime.UtcNow,
                Payload = "{\"version\":\"1.0\"}"
            };
            var command = new IngestClientEventCommand("PC-100", evtDto);

            _redisServiceMock.Setup(r => r.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string?)null);

            // Act
            var result = await handler.HandleAsync(command, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.True(result.Value);
            _auditRepoMock.Verify(r => r.AddAsync(It.IsAny<AuditEvent>(), It.IsAny<CancellationToken>()), Times.Once);
            _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task IngestClientEvent_DuplicateEventId_ShouldBeIgnoredIdempotently()
        {
            // Arrange
            var handler = new IngestClientEventCommandHandler(_auditRepoMock.Object, _unitOfWorkMock.Object, _redisServiceMock.Object);
            string eventId = Guid.NewGuid().ToString();
            var evtDto = new ClientEventEnvelopeDto
            {
                EventId = eventId,
                EventType = ClientEventType.ApplicationCrashed,
                ClientId = "PC-100",
                WorkstationId = "PC-100"
            };
            var command = new IngestClientEventCommand("PC-100", evtDto);

            _redisServiceMock.Setup(r => r.GetStringAsync($"v1:event:dedup:{eventId}", It.IsAny<CancellationToken>()))
                .ReturnsAsync("PROCESSED");

            // Act
            var result = await handler.HandleAsync(command, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess);
            _auditRepoMock.Verify(r => r.AddAsync(It.IsAny<AuditEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task IngestClientEvent_MismatchedClientId_ShouldBeRejected()
        {
            // Arrange
            var handler = new IngestClientEventCommandHandler(_auditRepoMock.Object, _unitOfWorkMock.Object, _redisServiceMock.Object);
            var evtDto = new ClientEventEnvelopeDto
            {
                EventId = Guid.NewGuid().ToString(),
                EventType = ClientEventType.SecurityEvent,
                ClientId = "PC-999", // Mismatched ClientId vs Connection PC-ID PC-100
                WorkstationId = "PC-100"
            };
            var command = new IngestClientEventCommand("PC-100", evtDto);

            // Act
            var result = await handler.HandleAsync(command, CancellationToken.None);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("ClientId does not match authenticated connection PC-ID.", result.ErrorCode);
            _auditRepoMock.Verify(r => r.AddAsync(It.IsAny<AuditEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
