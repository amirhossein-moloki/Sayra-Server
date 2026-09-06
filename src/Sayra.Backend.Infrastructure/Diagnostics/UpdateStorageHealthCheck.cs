using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Sayra.Backend.Application.Updates;

namespace Sayra.Backend.Infrastructure.Diagnostics
{
    public class UpdateStorageHealthCheck : IHealthCheck
    {
        private readonly IUpdateArtifactStorage _storage;

        public UpdateStorageHealthCheck(IUpdateArtifactStorage storage)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                // Probe storage availability safely
                bool probe = await _storage.ExistsAsync("__sayra_health_probe__", cancellationToken);

                return HealthCheckResult.Healthy("Artifact storage repository is online and responsive.");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("Artifact storage repository health check failed.", ex);
            }
        }
    }
}
