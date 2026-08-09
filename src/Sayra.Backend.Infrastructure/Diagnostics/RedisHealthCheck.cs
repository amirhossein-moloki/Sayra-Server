using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Sayra.Backend.Infrastructure.Diagnostics
{
    public class RedisHealthCheck : IHealthCheck
    {
        private readonly IConnectionMultiplexer _connectionMultiplexer;

        public RedisHealthCheck(IConnectionMultiplexer connectionMultiplexer)
        {
            _connectionMultiplexer = connectionMultiplexer ?? throw new ArgumentNullException(nameof(connectionMultiplexer));
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                var db = _connectionMultiplexer.GetDatabase();
                var pingTime = await db.PingAsync();

                return pingTime.TotalMilliseconds >= 0
                    ? HealthCheckResult.Healthy($"Redis response in {pingTime.TotalMilliseconds:F2}ms")
                    : HealthCheckResult.Unhealthy("Redis ping timed out or returned negative duration.");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("Redis health check failed", ex);
            }
        }
    }
}
