using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;
using Sayra.Backend.Application.Abstractions.Caching;
using Sayra.Backend.Application.Configuration.Models;
using Sayra.Backend.Domain;
using Sayra.Backend.Infrastructure.Caching;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class ConfigurationCacheUnitTests
    {
        private readonly Mock<IRedisService> _redisServiceMock;
        private readonly Mock<IConnectionMultiplexer> _multiplexerMock;
        private readonly Mock<IDatabase> _databaseMock;
        private readonly Mock<ILogger<ConfigurationCache>> _loggerMock;
        private readonly IOptions<ConfigurationCacheOptions> _options;

        public ConfigurationCacheUnitTests()
        {
            _redisServiceMock = new Mock<IRedisService>();
            _multiplexerMock = new Mock<IConnectionMultiplexer>();
            _databaseMock = new Mock<IDatabase>();
            _loggerMock = new Mock<ILogger<ConfigurationCache>>();

            _multiplexerMock.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_databaseMock.Object);

            _options = Options.Create(new ConfigurationCacheOptions
            {
                Enabled = true,
                EffectiveConfigurationTtlMinutes = 60,
                KeyPrefix = "sayra:config:v1:"
            });
        }

        private ConfigurationCache CreateCache()
        {
            return new ConfigurationCache(
                _redisServiceMock.Object,
                _multiplexerMock.Object,
                _options,
                _loggerMock.Object);
        }

        [Fact]
        public async Task GetEffectiveConfiguration_CacheMiss_ReturnsNull()
        {
            var orgId = Guid.NewGuid();
            var wsId = Guid.NewGuid();

            _redisServiceMock.Setup(r => r.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string?)null);

            var cache = CreateCache();
            var result = await cache.GetEffectiveConfigurationAsync(orgId, wsId, null, new List<Guid>());

            Assert.Null(result);
        }

        [Fact]
        public async Task SetAndGetEffectiveConfiguration_FreshCacheHit_ReturnsCachedItem()
        {
            var orgId = Guid.NewGuid();
            var wsId = Guid.NewGuid();

            var cachedConfig = new CachedEffectiveConfiguration
            {
                SchemaVersion = "1.0",
                EffectiveConfigurationJson = "{\"server\":{\"port\":5000}}",
                ScopeRevisions = new Dictionary<string, long> { { "Global", 0 }, { $"Workstation:{wsId}", 0 } }
            };

            string serialized = JsonSerializer.Serialize(cachedConfig);

            _redisServiceMock.Setup(r => r.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string key, CancellationToken ct) =>
                {
                    if (key.Contains("effective")) return serialized;
                    return "0"; // Scope revisions return 0
                });

            var cache = CreateCache();
            await cache.SetEffectiveConfigurationAsync(orgId, wsId, null, new List<Guid>(), cachedConfig);

            var retrieved = await cache.GetEffectiveConfigurationAsync(orgId, wsId, null, new List<Guid>());

            Assert.NotNull(retrieved);
            Assert.Equal("1.0", retrieved.SchemaVersion);
            Assert.Equal("{\"server\":{\"port\":5000}}", retrieved.EffectiveConfigurationJson);
        }

        [Fact]
        public async Task GetEffectiveConfiguration_StaleRevision_ReturnsNullAndInvalidates()
        {
            var orgId = Guid.NewGuid();
            var wsId = Guid.NewGuid();

            var cachedConfig = new CachedEffectiveConfiguration
            {
                SchemaVersion = "1.0",
                EffectiveConfigurationJson = "{\"server\":{\"port\":5000}}",
                ScopeRevisions = new Dictionary<string, long> { { "Global", 0 } }
            };

            string serialized = JsonSerializer.Serialize(cachedConfig);

            _redisServiceMock.Setup(r => r.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string key, CancellationToken ct) =>
                {
                    if (key.Contains("effective")) return serialized;
                    if (key.Contains("Global")) return "1"; // Global revision was incremented to 1!
                    return "0";
                });

            var cache = CreateCache();
            var retrieved = await cache.GetEffectiveConfigurationAsync(orgId, wsId, null, new List<Guid>());

            Assert.Null(retrieved); // Stale cache rejected!
            _redisServiceMock.Verify(r => r.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task RedisException_FallsBackToNullGracefully()
        {
            var orgId = Guid.NewGuid();
            var wsId = Guid.NewGuid();

            _redisServiceMock.Setup(r => r.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Redis offline"));

            var cache = CreateCache();
            var result = await cache.GetEffectiveConfigurationAsync(orgId, wsId, null, new List<Guid>());

            Assert.Null(result); // Degrades gracefully without throwing
        }

        [Fact]
        public async Task InvalidateScope_IncrementsRevisionCounter()
        {
            var orgId = Guid.NewGuid();
            var siteId = Guid.NewGuid();

            var cache = CreateCache();
            await cache.InvalidateScopeAsync(orgId, ConfigurationTargetType.Site, siteId);

            _redisServiceMock.Verify(r => r.IncrementAsync(
                It.Is<string>(k => k.Contains("Site") && k.Contains(siteId.ToString())),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
