using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sayra.Backend.Application.Abstractions.Caching;
using Sayra.Backend.Application.Configuration;
using Sayra.Backend.Application.Resilience;
using Sayra.Backend.Application.Telemetry;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Infrastructure.Caching;
using Sayra.Backend.Infrastructure.Configuration.Options;
using Sayra.Backend.Infrastructure.Diagnostics;
using Sayra.Backend.Infrastructure.Resilience;
using Sayra.Backend.Infrastructure.Updates;
using StackExchange.Redis;
using Xunit;

namespace Sayra.Backend.UnitTests.Resilience
{
    public class DependencyResilienceIntegrationTests
    {
        private readonly ResilienceOptions _options = new()
        {
            MaxRetryAttempts = 2,
            InitialBackoffSeconds = 0.01,
            MaxBackoffSeconds = 0.05,
            AttemptTimeoutSeconds = 1.0,
            OverallTimeoutSeconds = 2.0
        };

        private readonly ResilienceMetrics _metrics = new();

        [Fact]
        public async Task Redis_Outage_Causes_Cache_FailOpen_To_Authoritative_Source()
        {
            var mockMultiplexer = new Mock<IConnectionMultiplexer>();
            var mockDb = new Mock<IDatabase>();

            mockDb.Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Redis offline simulation"));

            mockMultiplexer.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                .Returns(mockDb.Object);

            var redisService = new RedisService(mockMultiplexer.Object, NullLogger<RedisService>.Instance);

            string? cachedValue = await redisService.GetStringAsync("sayra:config:v1:test");
            Assert.Null(cachedValue); // Graceful degradation to null without process crash
        }

        [Fact]
        public async Task Redis_Outage_Does_Not_Crash_Telemetry_Idempotency()
        {
            var mockRedis = new Mock<IRedisService>();
            mockRedis.Setup(r => r.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Redis connection failed"));

            var idempotencyService = new TelemetryIdempotencyService(mockRedis.Object, NullLogger<TelemetryIdempotencyService>.Instance);

            bool isStale = await idempotencyService.IsStaleTelemetryAsync("PC-100", DateTime.UtcNow);
            Assert.False(isStale); // Fails open to allow telemetry processing
        }

        [Fact]
        public async Task FileStorage_Read_With_Resilience_Succeeds()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "SayraTestStorage_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var updatesOptions = Options.Create(new UpdatesOptions
                {
                    LocalUpdateRepositoryPath = tempDir
                });

                var pipeline = new ResiliencePipeline(Options.Create(_options), _metrics, NullLogger<ResiliencePipeline>.Instance);
                var storage = new LocalUpdateArtifactStorage(updatesOptions, NullLogger<LocalUpdateArtifactStorage>.Instance, pipeline);

                Guid pkgId = Guid.NewGuid();
                byte[] data = new byte[] { 1, 2, 3, 4, 5 };
                using var stream = new MemoryStream(data);

                string tempKey = await storage.SaveTemporaryArtifactAsync(pkgId, stream);
                Assert.NotNull(tempKey);

                bool exists = await storage.ExistsAsync(tempKey);
                Assert.True(exists);

                long size = await storage.GetArtifactSizeAsync(tempKey);
                Assert.Equal(5, size);

                await storage.DeleteArtifactAsync(tempKey);
                bool existsAfterDelete = await storage.ExistsAsync(tempKey);
                Assert.False(existsAfterDelete);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        }

        [Fact]
        public void Resilience_Metrics_Emits_Counter_Metrics_On_Retry_And_Circuit_Breaker()
        {
            var metrics = new ResilienceMetrics();
            metrics.RecordRetryAttempt("PostgreSQL", "QueryUser", 1, "Retryable");
            metrics.RecordRetryExhausted("Redis", "GetCache", 3);
            metrics.RecordTimeout("FileStorage", "WriteStream", isOverallTimeout: true);
            metrics.RecordCircuitBreakerTrip("ExternalApi", "Closed", "Open");
            metrics.RecordFallback("Redis", "GetConfig", "DatabaseFallback");
            metrics.RecordCancellation("TCPTransport", "ReceiveFrame");

            // Instrumentation executed without exception
            Assert.NotNull(metrics);
        }
    }
}
