using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sayra.Backend.Application.Abstractions.Communication;

namespace Sayra.Backend.Infrastructure.Transport
{
    public class RemoteCommandTimeoutWorker : BackgroundService
    {
        private readonly Microsoft.Extensions.DependencyInjection.IServiceScopeFactory _scopeFactory;
        private readonly ILogger<RemoteCommandTimeoutWorker> _logger;
        private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(15);

        public RemoteCommandTimeoutWorker(
            Microsoft.Extensions.DependencyInjection.IServiceScopeFactory scopeFactory,
            ILogger<RemoteCommandTimeoutWorker> logger)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("RemoteCommandTimeoutWorker starting timeout evaluation background service (Interval: {Interval}s)...", _checkInterval.TotalSeconds);

            using var timer = new PeriodicTimer(_checkInterval);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (await timer.WaitForNextTickAsync(stoppingToken))
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var remoteCommandManager = scope.ServiceProvider.GetRequiredService<IRemoteCommandManager>();
                        await remoteCommandManager.EvaluateTimeoutsAsync(stoppingToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred during RemoteCommand timeout evaluation tick.");
                }
            }

            _logger.LogInformation("RemoteCommandTimeoutWorker background service stopped.");
        }
    }
}
