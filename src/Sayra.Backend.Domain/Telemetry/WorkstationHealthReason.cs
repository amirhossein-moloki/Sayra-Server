using System;
using Sayra.Backend.Domain.Enums;

namespace Sayra.Backend.Domain.Telemetry
{
    public sealed class WorkstationHealthReason
    {
        public string ReasonCode { get; set; } = string.Empty;
        public WorkstationHealthState Severity { get; set; } = WorkstationHealthState.Warning;
        public string Source { get; set; } = string.Empty;
        public double ObservedValue { get; set; }
        public double Threshold { get; set; }
        public string Message { get; set; } = string.Empty;
        public DateTime FirstDetectedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime LastObservedAtUtc { get; set; } = DateTime.UtcNow;
        public string? PolicyReference { get; set; }

        public WorkstationHealthReason()
        {
        }

        public WorkstationHealthReason(
            string reasonCode,
            WorkstationHealthState severity,
            string source,
            double observedValue,
            double threshold,
            string message,
            DateTime detectedAtUtc,
            string? policyReference = null)
        {
            ReasonCode = reasonCode ?? throw new ArgumentNullException(nameof(reasonCode));
            Severity = severity;
            Source = source ?? string.Empty;
            ObservedValue = observedValue;
            Threshold = threshold;
            Message = message ?? string.Empty;
            FirstDetectedAtUtc = detectedAtUtc;
            LastObservedAtUtc = detectedAtUtc;
            PolicyReference = policyReference;
        }
    }
}
