using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sayra.Backend.Application.Abstractions.Communication;

namespace Sayra.Backend.Infrastructure.Transport
{
    public class RemoteCommandTimeoutWorker : BackgroundService
    {
        private readonly IRemoteCommandManager _remoteCommandManager;
        private readonly ILogger<RemoteCommandTimeoutWorker> _logger;
        private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(15);

        public RemoteCommandTimeoutWorker(
            IRemoteCommandManager remoteCommandManager,
            ILogger<RemoteCommandTimeoutWorker> logger)
        {
            _remoteCommandManager = remoteCommandManager ?? throw new ArgumentNullException(nameof(remoteCommandManager));
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
                        await _remoteCommandManager.EvaluateTimeoutsAsync(stoppingToken);
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
