using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Sayra.Backend.Application.Updates;

namespace Sayra.Backend.Infrastructure.Diagnostics
{
    public class UpdateSigningHealthCheck : IHealthCheck
    {
        private readonly IUpdateSigningKeyProvider _signingKeyProvider;

        public UpdateSigningHealthCheck(IUpdateSigningKeyProvider signingKeyProvider)
        {
            _signingKeyProvider = signingKeyProvider ?? throw new ArgumentNullException(nameof(signingKeyProvider));
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                var activeKey = await _signingKeyProvider.GetActiveKeyAsync(cancellationToken);
                if (activeKey == null || string.IsNullOrWhiteSpace(activeKey.KeyId))
                {
                    return HealthCheckResult.Unhealthy("No active cryptographic signing key is available in the key registry.");
                }

                return HealthCheckResult.Healthy($"Active update signing key '{activeKey.KeyId}' ({activeKey.Algorithm}) is ready.");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("Update signing provider health check failed.", ex);
            }
        }
    }
}
