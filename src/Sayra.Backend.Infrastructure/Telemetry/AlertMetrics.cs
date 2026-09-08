using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using Sayra.Backend.Application.Telemetry;

namespace Sayra.Backend.Infrastructure.Telemetry
{
    public sealed class AlertMetrics : IAlertMetrics
    {
        public const string MeterName = "Sayra.Backend.Alerting";

        private readonly Meter _meter;
        private readonly Counter<long> _alertsEvaluatedCounter;
        private readonly Counter<long> _alertsTriggeredCounter;
        private readonly Counter<long> _incidentsCreatedCounter;
        private readonly Counter<long> _incidentsDeduplicatedCounter;
        private readonly Counter<long> _incidentsResolvedCounter;
        private readonly Counter<long> _incidentsSuppressedCounter;
        private readonly Counter<long> _evaluationFailuresCounter;
        private readonly Histogram<double> _evaluationDurationHistogram;

        public AlertMetrics()
        {
            _meter = new Meter(MeterName, "1.0.0");
            _alertsEvaluatedCounter = _meter.CreateCounter<long>("alerts_evaluated_total", description: "Total number of alert rules evaluated");
            _alertsTriggeredCounter = _meter.CreateCounter<long>("alerts_triggered_total", description: "Total number of alert rules triggered");
            _incidentsCreatedCounter = _meter.CreateCounter<long>("incidents_created_total", description: "Total number of incidents created");
            _incidentsDeduplicatedCounter = _meter.CreateCounter<long>("incidents_deduplicated_total", description: "Total number of duplicate incidents suppressed/deduplicated");
            _incidentsResolvedCounter = _meter.CreateCounter<long>("incidents_resolved_total", description: "Total number of incidents resolved");
            _incidentsSuppressedCounter = _meter.CreateCounter<long>("incidents_suppressed_total", description: "Total number of incidents suppressed by policy");
            _evaluationFailuresCounter = _meter.CreateCounter<long>("alert_evaluation_failures_total", description: "Total number of alert evaluation cycle failures");
            _evaluationDurationHistogram = _meter.CreateHistogram<double>("alert_evaluation_duration_seconds", unit: "s", description: "Histogram of alert evaluation cycle durations in seconds");
        }

        public void RecordAlertEvaluated(string ruleCode, string severity)
        {
            _alertsEvaluatedCounter.Add(1, new KeyValuePair<string, object?>("rule_code", ruleCode), new KeyValuePair<string, object?>("severity", severity));
        }

        public void RecordAlertTriggered(string ruleCode, string severity)
        {
            _alertsTriggeredCounter.Add(1, new KeyValuePair<string, object?>("rule_code", ruleCode), new KeyValuePair<string, object?>("severity", severity));
        }

        public void RecordIncidentCreated(string ruleCode, string severity)
        {
            _incidentsCreatedCounter.Add(1, new KeyValuePair<string, object?>("rule_code", ruleCode), new KeyValuePair<string, object?>("severity", severity));
        }

        public void RecordIncidentDeduplicated(string ruleCode)
        {
            _incidentsDeduplicatedCounter.Add(1, new KeyValuePair<string, object?>("rule_code", ruleCode));
        }

        public void RecordIncidentResolved(string ruleCode)
        {
            _incidentsResolvedCounter.Add(1, new KeyValuePair<string, object?>("rule_code", ruleCode));
        }

        public void RecordIncidentSuppressed(string ruleCode)
        {
            _incidentsSuppressedCounter.Add(1, new KeyValuePair<string, object?>("rule_code", ruleCode));
        }

        public void RecordEvaluationFailure()
        {
            _evaluationFailuresCounter.Add(1);
        }

        public void RecordEvaluationDuration(double durationSeconds)
        {
            _evaluationDurationHistogram.Record(durationSeconds);
        }
    }
}
