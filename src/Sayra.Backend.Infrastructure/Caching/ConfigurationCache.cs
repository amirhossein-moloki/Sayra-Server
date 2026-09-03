using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Sayra.Backend.Application.Abstractions.Caching;
using Sayra.Backend.Application.Configuration;
using Sayra.Backend.Application.Configuration.Models;
using Sayra.Backend.Domain;

#nullable enable

namespace Sayra.Backend.Infrastructure.Caching
{
    public class ConfigurationCache : IConfigurationCache
    {
        private readonly IRedisService _redisService;
        private readonly IConnectionMultiplexer _connectionMultiplexer;
        private readonly ConfigurationCacheOptions _options;
        private readonly ILogger<ConfigurationCache> _logger;

        private const string ReleaseLockLuaScript = @"
            if redis.call('get', KEYS[1]) == ARGV[1] then
                return redis.call('del', KEYS[1])
            else
                return 0
            end";

        public ConfigurationCache(
            IRedisService redisService,
            IConnectionMultiplexer connectionMultiplexer,
            IOptions<ConfigurationCacheOptions> options,
            ILogger<ConfigurationCache> logger)
        {
            _redisService = redisService ?? throw new ArgumentNullException(nameof(redisService));
            _connectionMultiplexer = connectionMultiplexer ?? throw new ArgumentNullException(nameof(connectionMultiplexer));
            _options = options?.Value ?? new ConfigurationCacheOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<CachedEffectiveConfiguration?> GetEffectiveConfigurationAsync(
            Guid organizationId,
            Guid workstationId,
            Guid? siteId,
            List<Guid> groupIds,
            CancellationToken cancellationToken = default)
        {
            if (!_options.Enabled) return null;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var key = ConfigurationCacheKeyBuilder.GetEffectiveConfigKey(_options.KeyPrefix, organizationId, workstationId);
                var json = await _redisService.GetStringAsync(key, cancellationToken);
                if (string.IsNullOrWhiteSpace(json))
                {
                    _logger.LogDebug("configuration_cache_miss for workstation {WorkstationId} in org {OrgId}", workstationId, organizationId);
                    return null;
                }

                var cachedConfig = JsonSerializer.Deserialize<CachedEffectiveConfiguration>(json);
                if (cachedConfig == null || cachedConfig.CacheSchemaVersion != 1)
                {
                    _logger.LogWarning("configuration_cache_error: Malformed cache payload or unknown schema version for workstation {WorkstationId}", workstationId);
                    await _redisService.RemoveAsync(key, cancellationToken);
                    return null;
                }

                // Check scope freshness
                bool isFresh = await CheckScopeFreshnessAsync(organizationId, workstationId, siteId, groupIds, cachedConfig, cancellationToken);
                if (!isFresh)
                {
                    _logger.LogInformation("configuration_cache_stale for workstation {WorkstationId} in org {OrgId}. Invalidating stale entry.", workstationId, organizationId);
                    await _redisService.RemoveAsync(key, cancellationToken);
                    return null;
                }

                _logger.LogDebug("configuration_cache_hit for workstation {WorkstationId} in org {OrgId}", workstationId, organizationId);
                return cachedConfig;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "configuration_cache_error while getting effective config for workstation {WorkstationId}. Falling back to DB.", workstationId);
                return null;
            }
        }

        public async Task SetEffectiveConfigurationAsync(
            Guid organizationId,
            Guid workstationId,
            Guid? siteId,
            List<Guid> groupIds,
            CachedEffectiveConfiguration cachedConfig,
            CancellationToken cancellationToken = default)
        {
            if (!_options.Enabled || cachedConfig == null) return;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Capture current scope revisions
                var currentRevisions = await FetchCurrentScopeRevisionsAsync(organizationId, workstationId, siteId, groupIds, cancellationToken);
                cachedConfig.ScopeRevisions = currentRevisions;
                cachedConfig.CachedAtUtc = DateTime.UtcNow;

                var key = ConfigurationCacheKeyBuilder.GetEffectiveConfigKey(_options.KeyPrefix, organizationId, workstationId);
                var json = JsonSerializer.Serialize(cachedConfig);
                var ttl = TimeSpan.FromMinutes(_options.EffectiveConfigurationTtlMinutes);

                await _redisService.SetStringAsync(key, json, ttl, cancellationToken);
                _logger.LogDebug("configuration_cache_set for workstation {WorkstationId} in org {OrgId}", workstationId, organizationId);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "configuration_cache_error while setting effective config for workstation {WorkstationId}.", workstationId);
            }
        }

        public async Task InvalidateWorkstationAsync(
            Guid organizationId,
            Guid workstationId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Increment workstation scope revision
                var revKey = ConfigurationCacheKeyBuilder.GetScopeRevisionKey(_options.KeyPrefix, organizationId, ConfigurationTargetType.Workstation, workstationId);
                await _redisService.IncrementAsync(revKey, TimeSpan.FromDays(30), cancellationToken);

                // Remove workstation effective key directly
                var key = ConfigurationCacheKeyBuilder.GetEffectiveConfigKey(_options.KeyPrefix, organizationId, workstationId);
                await _redisService.RemoveAsync(key, cancellationToken);

                _logger.LogInformation("configuration_cache_invalidation for workstation {WorkstationId} in org {OrgId}", workstationId, organizationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "configuration_cache_error during workstation invalidation for {WorkstationId}.", workstationId);
            }
        }

        public async Task InvalidateScopeAsync(
            Guid organizationId,
            ConfigurationTargetType targetType,
            Guid? targetId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var revKey = ConfigurationCacheKeyBuilder.GetScopeRevisionKey(_options.KeyPrefix, organizationId, targetType, targetId);
                await _redisService.IncrementAsync(revKey, TimeSpan.FromDays(30), cancellationToken);

                _logger.LogInformation("configuration_cache_invalidation for scope {TargetType}:{TargetId} in org {OrgId}", targetType, targetId, organizationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "configuration_cache_error during scope invalidation for {TargetType}:{TargetId}.", targetType, targetId);
            }
        }

        public async Task InvalidateTargetAsync(
            Guid organizationId,
            Guid targetId,
            CancellationToken cancellationToken = default)
        {
            await InvalidatePublicationMetadataAsync(organizationId, targetId, cancellationToken);
        }

        public async Task<CachedPublicationMetadata?> GetPublicationMetadataAsync(
            Guid organizationId,
            Guid targetId,
            CancellationToken cancellationToken = default)
        {
            if (!_options.Enabled) return null;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var key = ConfigurationCacheKeyBuilder.GetPublicationKey(_options.KeyPrefix, organizationId, targetId);
                var json = await _redisService.GetStringAsync(key, cancellationToken);
                if (string.IsNullOrWhiteSpace(json)) return null;

                var meta = JsonSerializer.Deserialize<CachedPublicationMetadata>(json);
                if (meta == null || meta.CacheSchemaVersion != 1) return null;

                return meta;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "configuration_cache_error while getting publication metadata for target {TargetId}.", targetId);
                return null;
            }
        }

        public async Task SetPublicationMetadataAsync(
            Guid organizationId,
            Guid targetId,
            CachedPublicationMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            if (!_options.Enabled || metadata == null) return;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var key = ConfigurationCacheKeyBuilder.GetPublicationKey(_options.KeyPrefix, organizationId, targetId);
                metadata.CachedAtUtc = DateTime.UtcNow;
                var json = JsonSerializer.Serialize(metadata);
                var ttl = TimeSpan.FromMinutes(_options.PublicationMetadataTtlMinutes);

                await _redisService.SetStringAsync(key, json, ttl, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "configuration_cache_error while setting publication metadata for target {TargetId}.", targetId);
            }
        }

        public async Task InvalidatePublicationMetadataAsync(
            Guid organizationId,
            Guid targetId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var key = ConfigurationCacheKeyBuilder.GetPublicationKey(_options.KeyPrefix, organizationId, targetId);
                await _redisService.RemoveAsync(key, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "configuration_cache_error while invalidating publication metadata for target {TargetId}.", targetId);
            }
        }

        public async Task<IDisposable?> AcquireStampedeLockAsync(
            Guid organizationId,
            Guid workstationId,
            CancellationToken cancellationToken = default)
        {
            if (!_options.Enabled) return new LockReleaser(null, null, null, _logger);

            try
            {
                var lockKey = ConfigurationCacheKeyBuilder.GetStampedeLockKey(_options.KeyPrefix, organizationId, workstationId);
                var lockValue = Guid.NewGuid().ToString();
                var lockTimeout = TimeSpan.FromSeconds(_options.LockTimeoutSeconds);
                var db = _connectionMultiplexer.GetDatabase();

                bool acquired = await db.StringSetAsync(lockKey, lockValue, lockTimeout, When.NotExists);
                if (acquired)
                {
                    return new LockReleaser(db, lockKey, lockValue, _logger);
                }

                _logger.LogDebug("configuration_cache_lock_contention for workstation {WorkstationId}", workstationId);

                // Short wait attempt
                var waitMs = Math.Min(_options.LockWaitTimeoutMs, 500);
                await Task.Delay(waitMs, cancellationToken);

                // Retry lock once
                acquired = await db.StringSetAsync(lockKey, lockValue, lockTimeout, When.NotExists);
                if (acquired)
                {
                    return new LockReleaser(db, lockKey, lockValue, _logger);
                }

                // If still not acquired, return null disposable so caller proceeds directly to DB resolution
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "configuration_cache_error while acquiring stampede lock for workstation {WorkstationId}.", workstationId);
                return null;
            }
        }

        private async Task<Dictionary<string, long>> FetchCurrentScopeRevisionsAsync(
            Guid organizationId,
            Guid workstationId,
            Guid? siteId,
            List<Guid>? groupIds,
            CancellationToken cancellationToken)
        {
            var result = new Dictionary<string, long>(StringComparer.Ordinal);

            // Global
            string globalKey = ConfigurationCacheKeyBuilder.GetScopeRevisionKey(_options.KeyPrefix, organizationId, ConfigurationTargetType.Global, null);
            result["Global"] = await GetRevisionValueAsync(globalKey, cancellationToken);

            // Site
            if (siteId.HasValue && siteId.Value != Guid.Empty)
            {
                string siteKey = ConfigurationCacheKeyBuilder.GetScopeRevisionKey(_options.KeyPrefix, organizationId, ConfigurationTargetType.Site, siteId.Value);
                result[$"Site:{siteId.Value}"] = await GetRevisionValueAsync(siteKey, cancellationToken);
            }

            // Groups
            if (groupIds != null)
            {
                foreach (var gid in groupIds)
                {
                    if (gid != Guid.Empty)
                    {
                        string groupKey = ConfigurationCacheKeyBuilder.GetScopeRevisionKey(_options.KeyPrefix, organizationId, ConfigurationTargetType.Group, gid);
                        result[$"Group:{gid}"] = await GetRevisionValueAsync(groupKey, cancellationToken);
                    }
                }
            }

            // Workstation
            string wsKey = ConfigurationCacheKeyBuilder.GetScopeRevisionKey(_options.KeyPrefix, organizationId, ConfigurationTargetType.Workstation, workstationId);
            result[$"Workstation:{workstationId}"] = await GetRevisionValueAsync(wsKey, cancellationToken);

            return result;
        }

        private async Task<bool> CheckScopeFreshnessAsync(
            Guid organizationId,
            Guid workstationId,
            Guid? siteId,
            List<Guid>? groupIds,
            CachedEffectiveConfiguration cachedConfig,
            CancellationToken cancellationToken)
        {
            var storedRevisions = cachedConfig.ScopeRevisions ?? new Dictionary<string, long>();

            // Global check
            string globalKey = ConfigurationCacheKeyBuilder.GetScopeRevisionKey(_options.KeyPrefix, organizationId, ConfigurationTargetType.Global, null);
            long currentGlobal = await GetRevisionValueAsync(globalKey, cancellationToken);
            storedRevisions.TryGetValue("Global", out long storedGlobal);
            if (currentGlobal > storedGlobal) return false;

            // Site check
            if (siteId.HasValue && siteId.Value != Guid.Empty)
            {
                string siteKey = ConfigurationCacheKeyBuilder.GetScopeRevisionKey(_options.KeyPrefix, organizationId, ConfigurationTargetType.Site, siteId.Value);
                long currentSite = await GetRevisionValueAsync(siteKey, cancellationToken);
                storedRevisions.TryGetValue($"Site:{siteId.Value}", out long storedSite);
                if (currentSite > storedSite) return false;
            }

            // Group checks
            if (groupIds != null)
            {
                foreach (var gid in groupIds)
                {
                    if (gid != Guid.Empty)
                    {
                        string groupKey = ConfigurationCacheKeyBuilder.GetScopeRevisionKey(_options.KeyPrefix, organizationId, ConfigurationTargetType.Group, gid);
                        long currentGroup = await GetRevisionValueAsync(groupKey, cancellationToken);
                        storedRevisions.TryGetValue($"Group:{gid}", out long storedGroup);
                        if (currentGroup > storedGroup) return false;
                    }
                }
            }

            // Workstation check
            string wsKey = ConfigurationCacheKeyBuilder.GetScopeRevisionKey(_options.KeyPrefix, organizationId, ConfigurationTargetType.Workstation, workstationId);
            long currentWs = await GetRevisionValueAsync(wsKey, cancellationToken);
            storedRevisions.TryGetValue($"Workstation:{workstationId}", out long storedWs);
            if (currentWs > storedWs) return false;

            return true;
        }

        private async Task<long> GetRevisionValueAsync(string key, CancellationToken cancellationToken)
        {
            var strVal = await _redisService.GetStringAsync(key, cancellationToken);
            if (long.TryParse(strVal, out long val))
            {
                return val;
            }
            return 0;
        }

        private class LockReleaser : IDisposable
        {
            private readonly IDatabase? _database;
            private readonly string? _key;
            private readonly string? _value;
            private readonly ILogger _logger;
            private int _disposed;

            public LockReleaser(IDatabase? database, string? key, string? value, ILogger logger)
            {
                _database = database;
                _key = key;
                _value = value;
                _logger = logger;
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
                if (_database == null || string.IsNullOrEmpty(_key) || string.IsNullOrEmpty(_value)) return;

                try
                {
                    _database.ScriptEvaluate(ReleaseLockLuaScript, new RedisKey[] { _key }, new RedisValue[] { _value });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to safely release stampede lock for key {Key}", _key);
                }
            }
        }
    }
}
