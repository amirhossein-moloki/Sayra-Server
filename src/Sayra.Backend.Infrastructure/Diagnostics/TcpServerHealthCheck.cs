using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Sayra.Backend.Application.Abstractions.Transport;

namespace Sayra.Backend.Infrastructure.Diagnostics
{
    public class TcpServerHealthCheck : IHealthCheck
    {
        private readonly ITcpServer _tcpServer;

        public TcpServerHealthCheck(ITcpServer tcpServer)
        {
            _tcpServer = tcpServer ?? throw new ArgumentNullException(nameof(tcpServer));
        }

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                if (_tcpServer.IsListening)
                {
                    return Task.FromResult(HealthCheckResult.Healthy(
                        $"TCP Server listener is active and running. Active connections: {_tcpServer.ActiveConnectionsCount}"));
                }

                return Task.FromResult(HealthCheckResult.Degraded("TCP Server listener is not active."));
            }
            catch (Exception ex)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy("TCP Server health check failed", ex));
            }
        }
    }
}
