using System;

namespace Sayra.Backend.Application.Telemetry
{
    public interface IAlertMetrics
    {
        void RecordAlertEvaluated(string ruleCode, string severity);
        void RecordAlertTriggered(string ruleCode, string severity);
        void RecordIncidentCreated(string ruleCode, string severity);
        void RecordIncidentDeduplicated(string ruleCode);
        void RecordIncidentResolved(string ruleCode);
        void RecordIncidentSuppressed(string ruleCode);
        void RecordEvaluationFailure();
        void RecordEvaluationDuration(double durationSeconds);
    }
}
