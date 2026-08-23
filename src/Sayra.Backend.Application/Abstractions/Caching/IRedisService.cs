using System;
using System.Threading;
using System.Threading.Tasks;

#nullable enable

namespace Sayra.Backend.Application.Abstractions.Caching
{
    public interface IRedisService
    {
        Task<string?> GetStringAsync(string key, CancellationToken cancellationToken = default);
        Task SetStringAsync(string key, string value, TimeSpan? expiry = null, CancellationToken cancellationToken = default);
        Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class;
        Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default) where T : class;
        Task<long> IncrementAsync(string key, TimeSpan? expiry = null, CancellationToken cancellationToken = default);
        Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default);
        Task<bool> PingAsync(CancellationToken cancellationToken = default);
    }
}
