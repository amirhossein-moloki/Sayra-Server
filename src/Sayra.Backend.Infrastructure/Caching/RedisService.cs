using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Sayra.Backend.Application.Abstractions.Caching;

#nullable enable

namespace Sayra.Backend.Infrastructure.Caching
{
    public class RedisService : IRedisService
    {
        private readonly IConnectionMultiplexer _connectionMultiplexer;
        private readonly IDatabase _database;
        private readonly ILogger<RedisService> _logger;

        public RedisService(IConnectionMultiplexer connectionMultiplexer, ILogger<RedisService> logger)
        {
            _connectionMultiplexer = connectionMultiplexer ?? throw new ArgumentNullException(nameof(connectionMultiplexer));
            _database = _connectionMultiplexer.GetDatabase();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<string?> GetStringAsync(string key, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var value = await _database.StringGetAsync(key);
                return value.HasValue ? value.ToString() : null;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Redis operation GetStringAsync canceled for key {Key}", MaskKey(key));
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Redis GetStringAsync failed for key {Key}. Degrading gracefully.", MaskKey(key));
                return null;
            }
        }

        public async Task SetStringAsync(string key, string value, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _database.StringSetAsync(key, value, expiry);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Redis operation SetStringAsync canceled for key {Key}", MaskKey(key));
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Redis SetStringAsync failed for key {Key}. Degrading gracefully.", MaskKey(key));
            }
        }

        public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var value = await GetStringAsync(key, cancellationToken);
                if (string.IsNullOrEmpty(value))
                {
                    return null;
                }

                return JsonSerializer.Deserialize<T>(value);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Deserialization failed for Redis key {Key}.", MaskKey(key));
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Redis GetAsync failed for key {Key}. Degrading gracefully.", MaskKey(key));
                return null;
            }
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default) where T : class
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (value == null) return;

                var json = JsonSerializer.Serialize(value);
                await SetStringAsync(key, json, expiry, cancellationToken);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Serialization failed for Redis key {Key}.", MaskKey(key));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Redis SetAsync failed for key {Key}. Degrading gracefully.", MaskKey(key));
            }
        }

        public async Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                return await _database.KeyDeleteAsync(key);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Redis operation RemoveAsync canceled for key {Key}", MaskKey(key));
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Redis RemoveAsync failed for key {Key}. Degrading gracefully.", MaskKey(key));
                return false;
            }
        }

        public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await _database.PingAsync();
                return result.TotalMilliseconds >= 0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis PingAsync failed.");
                return false;
            }
        }

        private static string MaskKey(string key)
        {
            // Redact potential security elements from keys in log files to prevent secret leakages
            if (string.IsNullOrEmpty(key)) return string.Empty;
            if (key.Contains("token") || key.Contains("secret") || key.Contains("key") || key.Contains("password"))
            {
                return "REDACTED_SENSITIVE_KEY";
            }
            return key;
        }
    }
}
