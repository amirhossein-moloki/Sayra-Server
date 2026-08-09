using System;
using System.Threading.Tasks;
using StackExchange.Redis;
using Sayra.Backend.Application.Abstractions.Caching;

namespace Sayra.Backend.Infrastructure.Caching
{
    public class RedisService : IRedisService
    {
        private readonly IConnectionMultiplexer _connectionMultiplexer;
        private readonly IDatabase _database;

        public RedisService(IConnectionMultiplexer connectionMultiplexer)
        {
            _connectionMultiplexer = connectionMultiplexer ?? throw new ArgumentNullException(nameof(connectionMultiplexer));
            _database = _connectionMultiplexer.GetDatabase();
        }

        public async Task<string?> GetStringAsync(string key)
        {
            var value = await _database.StringGetAsync(key);
            return value.HasValue ? value.ToString() : null;
        }

        public async Task SetStringAsync(string key, string value, TimeSpan? expiry = null)
        {
            await _database.StringSetAsync(key, value, expiry);
        }

        public async Task<bool> RemoveAsync(string key)
        {
            return await _database.KeyDeleteAsync(key);
        }

        public async Task<bool> PingAsync()
        {
            try
            {
                var result = await _database.PingAsync();
                return result.TotalMilliseconds >= 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
