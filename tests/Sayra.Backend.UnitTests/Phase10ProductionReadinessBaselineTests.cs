using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sayra.Backend.Application.Abstractions.Caching;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Telemetry;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain.Enums;
using Sayra.Backend.Domain.Telemetry;
using Sayra.Backend.Infrastructure.Configuration;
using Sayra.Backend.Infrastructure.Configuration.Options;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class Phase10ProductionReadinessBaselineTests
    {
        [Fact]
        public void ConfigurationValidator_EmptyDatabaseConnectionString_TriggersFailFastOnStartup()
        {
            // Arrange
            var dbOptions = new DatabaseOptions { ConnectionString = "" };
            var redisOptions = new RedisOptions { ConnectionString = "127.0.0.1:6379" };
            var serverOptions = new ServerOptions { Port = 5000, HandshakeTimeout = 10, ConnectionTimeout = 30, MaximumConnections = 100, ReceiveBufferSize = 4096, SendBufferSize = 4096, MaximumMessageSize = 1048576, HeartbeatInterval = 30, HeartbeatTimeout = 90, HeartbeatGracePeriod = 10, LivenessCheckInterval = 10 };
            var discoveryOptions = new DiscoveryOptions { UdpPort = 37020 };

            // Act & Assert
            var ex = Assert.Throws<InvalidOperationException>(() =>
                ConfigurationValidator.Validate(dbOptions, redisOptions, serverOptions, discoveryOptions));

            Assert.Contains("Database:ConnectionString", ex.Message);
        }

        [Fact]
        public void ConfigurationValidator_EmptyRedisConnectionString_TriggersFailFastOnStartup()
        {
            // Arrange
            var dbOptions = new DatabaseOptions { ConnectionString = "Host=127.0.0.1;Port=5432;Database=test;Username=test;Password=test" };
            var redisOptions = new RedisOptions { ConnectionString = "" };
            var serverOptions = new ServerOptions { Port = 5000, HandshakeTimeout = 10, ConnectionTimeout = 30, MaximumConnections = 100, ReceiveBufferSize = 4096, SendBufferSize = 4096, MaximumMessageSize = 1048576, HeartbeatInterval = 30, HeartbeatTimeout = 90, HeartbeatGracePeriod = 10, LivenessCheckInterval = 10 };
            var discoveryOptions = new DiscoveryOptions { UdpPort = 37020 };

            // Act & Assert
            var ex = Assert.Throws<InvalidOperationException>(() =>
                ConfigurationValidator.Validate(dbOptions, redisOptions, serverOptions, discoveryOptions));

            Assert.Contains("Redis:ConnectionString", ex.Message);
        }

        [Fact]
        public async Task TelemetryIdempotencyService_RedisFailure_FailsafeFallbackDoesNotThrow()
        {
            // Arrange
            var mockRedis = new Mock<IRedisService>();
            mockRedis.Setup(r => r.SetStringAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
                     .ThrowsAsync(new Exception("Redis connection reset"));
            mockRedis.Setup(r => r.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                     .ThrowsAsync(new Exception("Redis connection reset"));

            var service = new TelemetryIdempotencyService(mockRedis.Object, NullLogger<TelemetryIdempotencyService>.Instance);

            // Act
            var isDuplicate = await service.IsDuplicateEventAsync("EVT-TEST-101");
            var isStale = await service.IsStaleTelemetryAsync("PC-101", DateTime.UtcNow);

            // Assert
            Assert.False(isDuplicate); // Fails safe (allows processing)
            Assert.False(isStale);     // Fails safe (allows processing)

            // Verify Mark operations do not throw on Redis exception
            await service.MarkEventProcessedAsync("EVT-TEST-101");
            await service.RecordLatestTelemetryTimestampAsync("PC-101", DateTime.UtcNow);
        }

        [Fact]
        public async Task TelemetryIngestion_IdentityAntiSpoofing_RejectsMismatchBetweenConnectionAndPayload()
        {
            // Arrange
            var mockSecurityService = new Mock<ISecurityEventService>();
            var mockIdempotencyService = new Mock<ITelemetryIdempotencyService>();

            var ingestionService = new TelemetryIngestionService(
                mockSecurityService.Object,
                mockIdempotencyService.Object,
                NullLogger<TelemetryIngestionService>.Instance);

            var context = new TelemetryConnectionContext(
                connectionId: "CONN-100",
                pcId: "PC-100", // Authenticated connection is PC-100
                workstationId: Guid.NewGuid(),
                siteId: Guid.NewGuid(),
                organizationId: Guid.NewGuid(),
                remoteIpAddress: "127.0.0.1");

            var eventDto = new ClientEventEnvelopeDto
            {
                EventId = Guid.NewGuid().ToString(),
                EventType = "SESSION_STARTED",
                ClientId = "PC-999", // Spoofed payload claims PC-999
                WorkstationId = "PC-999",
                OccurredAt = DateTime.UtcNow,
                Payload = "{}"
            };

            // Act
            var result = await ingestionService.IngestOperationalEventAsync(context, eventDto);

            // Assert
            Assert.Equal(TelemetryIngestionStatus.IdentityMismatch, result.Status);
            Assert.Equal(TelemetryRejectionReason.IdentityMismatch, result.RejectionReason);

            mockSecurityService.Verify(s => s.RecordSecurityEventAsync(
                "TELEMETRY_IDENTITY_MISMATCH",
                It.IsAny<Guid?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                It.IsAny<string?>(),
                It.IsAny<Guid?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public void ServerOptions_HeartbeatTimeout_MustBeStrictlyGreaterThanHeartbeatInterval()
        {
            // Arrange
            var dbOptions = new DatabaseOptions { ConnectionString = "Host=127.0.0.1;Port=5432;Database=test;Username=test;Password=test" };
            var redisOptions = new RedisOptions { ConnectionString = "127.0.0.1:6379" };
            var serverOptions = new ServerOptions
            {
                Port = 5000,
                HandshakeTimeout = 10,
                ConnectionTimeout = 30,
                MaximumConnections = 100,
                ReceiveBufferSize = 4096,
                SendBufferSize = 4096,
                MaximumMessageSize = 1048576,
                HeartbeatInterval = 30,
                HeartbeatTimeout = 30, // Invalid: Equal to interval
                HeartbeatGracePeriod = 10,
                LivenessCheckInterval = 10
            };
            var discoveryOptions = new DiscoveryOptions { UdpPort = 37020 };

            // Act & Assert
            var ex = Assert.Throws<InvalidOperationException>(() =>
                ConfigurationValidator.Validate(dbOptions, redisOptions, serverOptions, discoveryOptions));

            Assert.Contains("Server:HeartbeatTimeout", ex.Message);
        }
    }
}
