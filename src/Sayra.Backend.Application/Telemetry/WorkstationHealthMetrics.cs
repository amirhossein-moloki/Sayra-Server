using System.Diagnostics.Metrics;

namespace Sayra.Backend.Application.Telemetry
{
    public interface IWorkstationHealthMetrics
    {
        void RecordEvaluationRun(double durationSeconds, int totalEvaluated, int healthyCount, int warningCount, int degradedCount, int criticalCount, int offlineCount);
        void RecordEvaluationFailure();
        void RecordHealthStateTransition(string fromState, string toState);
    }

    public sealed class WorkstationHealthMetrics : IWorkstationHealthMetrics
    {
        public const string MeterName = "Sayra.Backend.Health";

        private readonly Counter<long> _evaluationsTotal;
        private readonly Counter<long> _evaluationFailuresTotal;
        private readonly Counter<long> _evaluationsWorkstationsTotal;
        private readonly Counter<long> _stateTransitionsTotal;
        private readonly Histogram<double> _evaluationDurationSeconds;

        public WorkstationHealthMetrics(IMeterFactory meterFactory)
        {
            var meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);

            _evaluationsTotal = meter.CreateCounter<long>("health_evaluation_runs_total", description: "Total background workstation health evaluation runs executed.");
            _evaluationFailuresTotal = meter.CreateCounter<long>("health_evaluation_failures_total", description: "Total failed background workstation health evaluation runs.");
            _evaluationsWorkstationsTotal = meter.CreateCounter<long>("health_evaluated_workstations_total", description: "Total individual workstation health evaluations completed.");
            _stateTransitionsTotal = meter.CreateCounter<long>("health_state_transitions_total", description: "Total workstation health state transitions.");
            _evaluationDurationSeconds = meter.CreateHistogram<double>("health_evaluation_duration_seconds", unit: "s", description: "Duration of workstation health evaluation execution runs in seconds.");
        }

        public void RecordEvaluationRun(double durationSeconds, int totalEvaluated, int healthyCount, int warningCount, int degradedCount, int criticalCount, int offlineCount)
        {
            _evaluationsTotal.Add(1);
            _evaluationDurationSeconds.Record(durationSeconds);
            _evaluationsWorkstationsTotal.Add(totalEvaluated);
        }

        public void RecordEvaluationFailure()
        {
            _evaluationFailuresTotal.Add(1);
        }

        public void RecordHealthStateTransition(string fromState, string toState)
        {
            _stateTransitionsTotal.Add(1, new KeyValuePair<string, object?>("from_state", fromState), new KeyValuePair<string, object?>("to_state", toState));
        }
    }
}
