using System;
using System.Threading.Tasks;

#nullable enable

namespace Sayra.Backend.Application.Abstractions.Caching
{
    public interface IRedisService
    {
        Task<string?> GetStringAsync(string key);
        Task SetStringAsync(string key, string value, TimeSpan? expiry = null);
        Task<bool> RemoveAsync(string key);
        Task<bool> PingAsync();
    }
}
