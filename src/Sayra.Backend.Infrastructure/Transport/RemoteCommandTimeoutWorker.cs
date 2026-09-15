using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sayra.Backend.Application.Abstractions.Communication;
using Sayra.Backend.Application.Abstractions.Diagnostics;

namespace Sayra.Backend.Infrastructure.Transport
{
    public class RemoteCommandTimeoutWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<RemoteCommandTimeoutWorker> _logger;
        private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(15);

        public RemoteCommandTimeoutWorker(
            IServiceScopeFactory scopeFactory,
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
                        await PerformTimeoutEvaluationCycleAsync(stoppingToken);
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

        public async Task PerformTimeoutEvaluationCycleAsync(CancellationToken cancellationToken)
        {
            var sw = Stopwatch.StartNew();
            bool isSuccess = true;
            using var scope = _scopeFactory.CreateScope();
            var metrics = scope.ServiceProvider.GetService<IWorkerMetrics>();

            try
            {
                metrics?.RecordWorkerActiveState(nameof(RemoteCommandTimeoutWorker), true);
                var remoteCommandManager = scope.ServiceProvider.GetRequiredService<IRemoteCommandManager>();
                await remoteCommandManager.EvaluateTimeoutsAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Graceful cancellation on shutdown
            }
            catch (Exception ex)
            {
                isSuccess = false;
                metrics?.RecordWorkerError(nameof(RemoteCommandTimeoutWorker), ex.GetType().Name);
                _logger.LogError(ex, "Error occurred during RemoteCommand timeout evaluation cycle.");
            }
            finally
            {
                sw.Stop();
                metrics?.RecordWorkerRun(nameof(RemoteCommandTimeoutWorker), sw.Elapsed.TotalSeconds, isSuccess);
                metrics?.RecordWorkerActiveState(nameof(RemoteCommandTimeoutWorker), false);
            }
        }
    }
}
