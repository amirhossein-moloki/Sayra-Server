using System;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Telemetry;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Infrastructure.Configuration.Options;

namespace Sayra.Backend.Infrastructure.Telemetry
{
    public class TelemetryAggregationWorker : BackgroundService
    {
        private static readonly Meter TelemetryMeter = new Meter("Sayra.Backend.Telemetry");
        private static readonly Counter<long> AggregationRunsCounter = TelemetryMeter.CreateCounter<long>("telemetry_aggregation_runs_total");
        private static readonly Counter<long> AggregationErrorsCounter = TelemetryMeter.CreateCounter<long>("telemetry_aggregation_errors_total");
        private static readonly Counter<long> AggregateRowsWrittenCounter = TelemetryMeter.CreateCounter<long>("telemetry_aggregate_rows_written_total");

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly TelemetryAggregationOptions _options;
        private readonly ILogger<TelemetryAggregationWorker> _logger;

        public TelemetryAggregationWorker(
            IServiceScopeFactory scopeFactory,
            IOptions<TelemetryAggregationOptions> options,
            ILogger<TelemetryAggregationWorker> logger)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.Enabled)
            {
                _logger.LogInformation("TelemetryAggregationWorker is disabled via configuration.");
                return;
            }

            int checkIntervalSeconds = Math.Max(1, _options.IntervalSeconds);
            _logger.LogInformation("TelemetryAggregationWorker started with execution interval {Interval}s, batch size {BatchSize}.",
                checkIntervalSeconds, _options.BatchSize);

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(checkIntervalSeconds));

            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    await PerformAggregationCycleAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("TelemetryAggregationWorker is shutting down gracefully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in TelemetryAggregationWorker execution loop.");
            }
        }

        public async Task PerformAggregationCycleAsync(CancellationToken cancellationToken)
        {
            AggregationRunsCounter.Add(1);
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var historyRepo = scope.ServiceProvider.GetRequiredService<ITelemetryHistoryRepository>();
                var aggregateRepo = scope.ServiceProvider.GetRequiredService<ITelemetryAggregateRepository>();
                var aggregationService = scope.ServiceProvider.GetRequiredService<ITelemetryAggregationService>();

                // Process 1m raw -> 1m aggregate
                await ProcessRawTo1mAggregatesAsync(historyRepo, aggregateRepo, aggregationService, cancellationToken);

                // Process 1m -> 5m rollups
                await ProcessRollupsAsync(aggregateRepo, aggregationService, "1m", "5m", cancellationToken);

                // Process 5m -> 1h rollups
                await ProcessRollupsAsync(aggregateRepo, aggregationService, "5m", "1h", cancellationToken);
            }
            catch (Exception ex)
            {
                AggregationErrorsCounter.Add(1);
                _logger.LogError(ex, "Error occurred during telemetry aggregation cycle.");
            }
        }

        private async Task ProcessRawTo1mAggregatesAsync(
            ITelemetryHistoryRepository historyRepo,
            ITelemetryAggregateRepository aggregateRepo,
            ITelemetryAggregationService aggregationService,
            CancellationToken cancellationToken)
        {
            var checkpoint = await aggregateRepo.GetCheckpointAsync("1m", cancellationToken);
            DateTime lastProcessed = checkpoint?.LastProcessedServerTimestamp ?? DateTime.UtcNow.AddHours(-1);

            DateTime from = lastProcessed;
            DateTime to = DateTime.UtcNow;

            var rawRecords = await historyRepo.GetHistoryAllAsync(
                from, to, _options.BatchSize, cancellationToken);

            // Note: If no organization filter, we fetch records across workstations
            if (rawRecords.Count == 0)
            {
                return;
            }

            var aggregates = aggregationService.AggregateRawRecords(rawRecords, "1m");
            if (aggregates.Count > 0)
            {
                await aggregateRepo.SaveAggregatesBatchAsync(aggregates, cancellationToken);
                AggregateRowsWrittenCounter.Add(aggregates.Count);

                DateTime maxServerTs = rawRecords.Max(r => r.ServerReceivedAt);
                DateTime maxWinEnd = aggregates.Max(a => a.WindowEnd);

                checkpoint ??= new TelemetryAggregationCheckpoint { Granularity = "1m" };
                checkpoint.LastProcessedServerTimestamp = maxServerTs;
                checkpoint.LastProcessedWindowEnd = maxWinEnd;
                checkpoint.RecordsProcessed += rawRecords.Count;

                await aggregateRepo.SaveCheckpointAsync(checkpoint, cancellationToken);
            }
        }

        private async Task ProcessRollupsAsync(
            ITelemetryAggregateRepository aggregateRepo,
            ITelemetryAggregationService aggregationService,
            string sourceGranularity,
            string targetGranularity,
            CancellationToken cancellationToken)
        {
            var checkpoint = await aggregateRepo.GetCheckpointAsync(targetGranularity, cancellationToken);
            DateTime lastProcessed = checkpoint?.LastProcessedWindowEnd ?? DateTime.UtcNow.AddHours(-6);

            var sourceAggregates = await aggregateRepo.GetAggregatesAllAsync(
                sourceGranularity, lastProcessed, DateTime.UtcNow, _options.BatchSize, cancellationToken);

            if (sourceAggregates.Count == 0)
            {
                return;
            }

            var rollups = aggregationService.RollupAggregates(sourceAggregates, targetGranularity);
            if (rollups.Count > 0)
            {
                await aggregateRepo.SaveAggregatesBatchAsync(rollups, cancellationToken);
                AggregateRowsWrittenCounter.Add(rollups.Count);

                DateTime maxWinEnd = rollups.Max(r => r.WindowEnd);

                checkpoint ??= new TelemetryAggregationCheckpoint { Granularity = targetGranularity };
                checkpoint.LastProcessedWindowEnd = maxWinEnd;
                checkpoint.RecordsProcessed += sourceAggregates.Count;

                await aggregateRepo.SaveCheckpointAsync(checkpoint, cancellationToken);
            }
        }
    }
}
