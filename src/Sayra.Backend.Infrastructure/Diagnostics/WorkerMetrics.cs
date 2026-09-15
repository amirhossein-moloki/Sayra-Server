using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Sayra.Backend.Application.Abstractions.Diagnostics;

namespace Sayra.Backend.Infrastructure.Diagnostics
{
    public sealed class WorkerMetrics : IWorkerMetrics
    {
        public const string MeterName = "Sayra.Backend.Workers";

        private readonly Meter _meter;
        private readonly Counter<long> _runsCounter;
        private readonly Counter<long> _errorsCounter;
        private readonly Counter<long> _itemsCounter;
        private readonly Histogram<double> _durationHistogram;
        private readonly ConcurrentDictionary<string, int> _activeStates = new();

        public WorkerMetrics(IMeterFactory? meterFactory = null)
        {
            _meter = meterFactory?.Create(MeterName, "1.0.0") ?? new Meter(MeterName, "1.0.0");

            _runsCounter = _meter.CreateCounter<long>(
                "worker_runs_total",
                unit: "{run}",
                description: "Total number of background worker execution cycles.");

            _errorsCounter = _meter.CreateCounter<long>(
                "worker_errors_total",
                unit: "{error}",
                description: "Total number of background worker execution errors.");

            _itemsCounter = _meter.CreateCounter<long>(
                "worker_items_processed_total",
                unit: "{item}",
                description: "Total number of work items processed by background workers.");

            _durationHistogram = _meter.CreateHistogram<double>(
                "worker_duration_seconds",
                unit: "s",
                description: "Execution duration of background worker cycles in seconds.");

            _meter.CreateObservableGauge(
                "worker_active_state",
                () =>
                {
                    var tags = new System.Collections.Generic.List<Measurement<int>>();
                    foreach (var kvp in _activeStates)
                    {
                        tags.Add(new Measurement<int>(kvp.Value, new KeyValuePair<string, object?>("worker_name", kvp.Key)));
                    }
                    return tags;
                },
                unit: "{state}",
                description: "Active state (1 for active/running, 0 for stopped) of background workers.");
        }

        public void RecordWorkerRun(string workerName, double durationSeconds, bool isSuccess)
        {
            var tags = new TagList
            {
                { "worker_name", workerName ?? "unknown" },
                { "result", isSuccess ? "success" : "failure" }
            };

            _runsCounter.Add(1, tags);
            _durationHistogram.Record(durationSeconds, tags);
        }

        public void RecordWorkerError(string workerName, string errorType)
        {
            var tags = new TagList
            {
                { "worker_name", workerName ?? "unknown" },
                { "error_type", errorType ?? "unknown" }
            };

            _errorsCounter.Add(1, tags);
        }

        public void RecordWorkerActiveState(string workerName, bool isActive)
        {
            if (string.IsNullOrEmpty(workerName)) return;
            _activeStates[workerName] = isActive ? 1 : 0;
        }

        public void RecordItemsProcessed(string workerName, long count)
        {
            if (count <= 0) return;
            var tags = new TagList
            {
                { "worker_name", workerName ?? "unknown" }
            };

            _itemsCounter.Add(count, tags);
        }
    }
}
