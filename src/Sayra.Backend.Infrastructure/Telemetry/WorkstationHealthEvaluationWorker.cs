using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sayra.Backend.Application.Telemetry;

namespace Sayra.Backend.Infrastructure.Telemetry
{
    public sealed class WorkstationHealthEvaluationWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IOptions<WorkstationHealthPolicyOptions> _options;
        private readonly ILogger<WorkstationHealthEvaluationWorker> _logger;

        public WorkstationHealthEvaluationWorker(
            IServiceScopeFactory scopeFactory,
            IOptions<WorkstationHealthPolicyOptions> options,
            ILogger<WorkstationHealthEvaluationWorker> logger)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Workstation Health Evaluation Worker starting. Interval: {IntervalSeconds}s, BatchSize: {BatchSize}.",
                _options.Value.EvaluationIntervalSeconds, _options.Value.EvaluationBatchSize);

            while (!stoppingToken.IsCancellationRequested)
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    await PerformEvaluationCycleAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled exception occurred during workstation health evaluation cycle.");
                    using (var errScope = _scopeFactory.CreateScope())
                    {
                        var metrics = errScope.ServiceProvider.GetService<IWorkstationHealthMetrics>();
                        metrics?.RecordEvaluationFailure();
                    }
                }
                finally
                {
                    sw.Stop();
                }

                var delayTime = _options.Value.EvaluationInterval - sw.Elapsed;
                if (delayTime > TimeSpan.Zero)
                {
                    try
                    {
                        await Task.Delay(delayTime, stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }

            _logger.LogInformation("Workstation Health Evaluation Worker stopped.");
        }

        public async Task PerformEvaluationCycleAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var stateReader = scope.ServiceProvider.GetRequiredService<IWorkstationStateReader>();
            var healthStore = scope.ServiceProvider.GetRequiredService<IWorkstationHealthStore>();
            var evaluator = scope.ServiceProvider.GetRequiredService<IWorkstationHealthEvaluator>();
            var metrics = scope.ServiceProvider.GetService<IWorkstationHealthMetrics>();

            var sw = Stopwatch.StartNew();

            // Fetch all currently tracked workstation states
            var states = await stateReader.GetWorkstationStatesAsync(null, null, cancellationToken);
            if (states == null || states.Count == 0)
            {
                sw.Stop();
                metrics?.RecordEvaluationRun(sw.Elapsed.TotalSeconds, 0, 0, 0, 0, 0, 0);
                return;
            }

            int batchSize = _options.Value.EvaluationBatchSize <= 0 ? 100 : _options.Value.EvaluationBatchSize;
            int healthy = 0, warning = 0, degraded = 0, critical = 0, offline = 0, unknown = 0;

            for (int i = 0; i < states.Count; i += batchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var batch = states.Skip(i).Take(batchSize).ToList();

                foreach (var state in batch)
                {
                    if (string.IsNullOrWhiteSpace(state.PcId)) continue;

                    var previousHealth = await healthStore.GetHealthResultAsync(state.PcId, cancellationToken);
                    var newHealth = await evaluator.EvaluateWorkstationHealthAsync(state, previousHealth, _options.Value, cancellationToken);

                    if (previousHealth != null && previousHealth.HealthState != newHealth.HealthState)
                    {
                        metrics?.RecordHealthStateTransition(previousHealth.HealthState.ToString(), newHealth.HealthState.ToString());
                        _logger.LogInformation("Workstation {PcId} health state transitioned from {OldState} to {NewState} (Score: {Score:F0}).",
                            state.PcId, previousHealth.HealthState, newHealth.HealthState, newHealth.HealthScore);
                    }

                    await healthStore.SaveHealthResultAsync(newHealth, cancellationToken);

                    switch (newHealth.HealthState)
                    {
                        case Domain.Enums.WorkstationHealthState.Healthy: healthy++; break;
                        case Domain.Enums.WorkstationHealthState.Warning: warning++; break;
                        case Domain.Enums.WorkstationHealthState.Degraded: degraded++; break;
                        case Domain.Enums.WorkstationHealthState.Critical: critical++; break;
                        case Domain.Enums.WorkstationHealthState.Offline: offline++; break;
                        default: unknown++; break;
                    }
                }
            }

            sw.Stop();
            metrics?.RecordEvaluationRun(sw.Elapsed.TotalSeconds, states.Count, healthy, warning, degraded, critical, offline);
            _logger.LogDebug("Completed workstation health evaluation cycle for {Count} workstations in {DurationMs}ms.",
                states.Count, sw.ElapsedMilliseconds);
        }
    }
}
