using System;
using Sayra.Backend.Domain.Enums;

namespace Sayra.Backend.Domain.Entities
{
    public class Incident : BaseEntity
    {
        public string Fingerprint { get; private set; } = string.Empty;
        public string RuleCode { get; private set; } = string.Empty;
        public Guid OrganizationId { get; private set; }
        public Guid? SiteId { get; private set; }
        public Guid? WorkstationId { get; private set; }
        public string PcId { get; private set; } = string.Empty;
        public AlertSeverity Severity { get; private set; } = AlertSeverity.Warning;
        public IncidentLifecycleState LifecycleState { get; private set; } = IncidentLifecycleState.Triggered;
        public DateTime FirstTriggeredAtUtc { get; private set; } = DateTime.UtcNow;
        public DateTime? FiringAtUtc { get; private set; }
        public DateTime LastObservedAtUtc { get; private set; } = DateTime.UtcNow;
        public DateTime? ResolvedAtUtc { get; private set; }
        public string ReasonCode { get; private set; } = string.Empty;
        public string Source { get; private set; } = string.Empty;
        public string Title { get; private set; } = string.Empty;
        public string Description { get; private set; } = string.Empty;
        public string TriggerEvidence { get; private set; } = string.Empty;
        public string? RecoveryEvidence { get; private set; }
        public string? PolicyVersion { get; private set; }
        public bool IsSuppressed { get; private set; }
        public string? SuppressionReason { get; private set; }
        public int ObservationCount { get; private set; } = 1;
        public uint RowVersion { get; set; }

        protected Incident()
        {
        }

        public static Incident CreateTriggered(
            string fingerprint,
            string ruleCode,
            Guid organizationId,
            Guid? siteId,
            Guid? workstationId,
            string pcId,
            AlertSeverity severity,
            string reasonCode,
            string source,
            string title,
            string description,
            string triggerEvidence,
            DateTime triggeredAtUtc,
            string? policyVersion = null,
            bool isSuppressed = false,
            string? suppressionReason = null)
        {
            if (string.IsNullOrWhiteSpace(fingerprint)) throw new ArgumentNullException(nameof(fingerprint));
            if (string.IsNullOrWhiteSpace(ruleCode)) throw new ArgumentNullException(nameof(ruleCode));
            if (string.IsNullOrWhiteSpace(pcId)) throw new ArgumentNullException(nameof(pcId));

            return new Incident
            {
                Id = Guid.NewGuid(),
                Fingerprint = fingerprint,
                RuleCode = ruleCode,
                OrganizationId = organizationId,
                SiteId = siteId,
                WorkstationId = workstationId,
                PcId = pcId,
                Severity = severity,
                LifecycleState = IncidentLifecycleState.Triggered,
                FirstTriggeredAtUtc = triggeredAtUtc,
                LastObservedAtUtc = triggeredAtUtc,
                ReasonCode = reasonCode ?? string.Empty,
                Source = source ?? string.Empty,
                Title = title ?? string.Empty,
                Description = description ?? string.Empty,
                TriggerEvidence = triggerEvidence ?? string.Empty,
                PolicyVersion = policyVersion,
                IsSuppressed = isSuppressed,
                SuppressionReason = suppressionReason,
                ObservationCount = 1,
                CreatedAt = triggeredAtUtc,
                UpdatedAt = triggeredAtUtc
            };
        }

        public static Incident CreateFiring(
            string fingerprint,
            string ruleCode,
            Guid organizationId,
            Guid? siteId,
            Guid? workstationId,
            string pcId,
            AlertSeverity severity,
            string reasonCode,
            string source,
            string title,
            string description,
            string triggerEvidence,
            DateTime firingAtUtc,
            string? policyVersion = null,
            bool isSuppressed = false,
            string? suppressionReason = null)
        {
            var incident = CreateTriggered(
                fingerprint,
                ruleCode,
                organizationId,
                siteId,
                workstationId,
                pcId,
                severity,
                reasonCode,
                source,
                title,
                description,
                triggerEvidence,
                firingAtUtc,
                policyVersion,
                isSuppressed,
                suppressionReason);

            incident.TransitionToFiring(firingAtUtc);
            return incident;
        }

        public void TransitionToFiring(DateTime firingAtUtc)
        {
            if (LifecycleState == IncidentLifecycleState.Resolved)
            {
                throw new InvalidOperationException("Cannot transition a resolved incident directly to firing without re-triggering.");
            }

            LifecycleState = IncidentLifecycleState.Firing;
            FiringAtUtc = firingAtUtc;
            LastObservedAtUtc = firingAtUtc;
            UpdatedAt = firingAtUtc;
        }

        public void RecordObservation(
            string updatedEvidence,
            DateTime observedAtUtc,
            TimeSpan? minSustainedDuration = null,
            AlertSeverity? newSeverity = null)
        {
            if (LifecycleState == IncidentLifecycleState.Resolved)
            {
                LifecycleState = IncidentLifecycleState.Triggered;
                FirstTriggeredAtUtc = observedAtUtc;
                ResolvedAtUtc = null;
                RecoveryEvidence = null;
                ObservationCount = 1;
            }
            else
            {
                ObservationCount++;
            }

            LastObservedAtUtc = observedAtUtc;
            UpdatedAt = observedAtUtc;
            if (!string.IsNullOrWhiteSpace(updatedEvidence))
            {
                TriggerEvidence = updatedEvidence;
            }

            if (newSeverity.HasValue)
            {
                Severity = newSeverity.Value;
            }

            if (LifecycleState == IncidentLifecycleState.Triggered)
            {
                var sustained = observedAtUtc - FirstTriggeredAtUtc;
                if (!minSustainedDuration.HasValue || minSustainedDuration.Value <= TimeSpan.Zero || sustained >= minSustainedDuration.Value)
                {
                    TransitionToFiring(observedAtUtc);
                }
            }
        }

        public void Resolve(string recoveryEvidence, DateTime resolvedAtUtc)
        {
            if (LifecycleState == IncidentLifecycleState.Resolved)
            {
                return;
            }

            LifecycleState = IncidentLifecycleState.Resolved;
            ResolvedAtUtc = resolvedAtUtc;
            RecoveryEvidence = recoveryEvidence ?? string.Empty;
            LastObservedAtUtc = resolvedAtUtc;
            UpdatedAt = resolvedAtUtc;
        }

        public void Suppress(string suppressionReason)
        {
            IsSuppressed = true;
            SuppressionReason = suppressionReason ?? "Policy suppression active";
            UpdatedAt = DateTime.UtcNow;
        }

        public void Unsuppress()
        {
            IsSuppressed = false;
            SuppressionReason = null;
            UpdatedAt = DateTime.UtcNow;
        }
    }
}
