using System;
using Sayra.Backend.Domain.Enums;

namespace Sayra.Backend.Domain.Telemetry
{
    public sealed class AlertRule
    {
        public string RuleCode { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;
        public AlertSeverity Severity { get; set; } = AlertSeverity.Warning;
        public int CooldownSeconds { get; set; } = 300;
        public bool SuppressionEnabled { get; set; } = true;
        public int MinSustainedSeconds { get; set; } = 0;
        public string TriggerSource { get; set; } = "HealthEvaluator";
        public double Threshold { get; set; } = 0.0;
        public string? PolicyVersion { get; set; }

        public AlertRule()
        {
        }

        public AlertRule(
            string ruleCode,
            string name,
            string description,
            AlertSeverity severity,
            int cooldownSeconds = 300,
            bool suppressionEnabled = true,
            int minSustainedSeconds = 0,
            string triggerSource = "HealthEvaluator",
            double threshold = 0.0,
            string? policyVersion = null,
            bool isEnabled = true)
        {
            RuleCode = ruleCode ?? throw new ArgumentNullException(nameof(ruleCode));
            Name = name ?? string.Empty;
            Description = description ?? string.Empty;
            Severity = severity;
            CooldownSeconds = cooldownSeconds;
            SuppressionEnabled = suppressionEnabled;
            MinSustainedSeconds = minSustainedSeconds;
            TriggerSource = triggerSource ?? "HealthEvaluator";
            Threshold = threshold;
            PolicyVersion = policyVersion;
            IsEnabled = isEnabled;
        }
    }
}
