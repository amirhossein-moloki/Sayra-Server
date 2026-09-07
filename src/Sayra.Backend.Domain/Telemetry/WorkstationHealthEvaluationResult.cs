using System;
using System.Collections.Generic;
using Sayra.Backend.Domain.Enums;
using Sayra.Backend.Domain.ValueObjects;

namespace Sayra.Backend.Domain.Telemetry
{
    public sealed class WorkstationHealthEvaluationResult
    {
        public WorkstationIdentity Identity { get; set; }
        public WorkstationHealthState HealthState { get; set; } = WorkstationHealthState.Unknown;
        public double HealthScore { get; set; } = 100.0;
        public List<WorkstationHealthReason> Reasons { get; set; } = new List<WorkstationHealthReason>();
        public DateTime EvaluatedAtUtc { get; set; } = DateTime.UtcNow;
        public string? PolicyVersion { get; set; }

        public WorkstationHealthEvaluationResult()
        {
            Identity = new WorkstationIdentity("UNKNOWN");
        }

        public WorkstationHealthEvaluationResult(
            WorkstationIdentity identity,
            WorkstationHealthState healthState,
            double healthScore,
            List<WorkstationHealthReason> reasons,
            DateTime evaluatedAtUtc,
            string? policyVersion = null)
        {
            Identity = identity ?? throw new ArgumentNullException(nameof(identity));
            HealthState = healthState;
            HealthScore = healthScore;
            Reasons = reasons ?? new List<WorkstationHealthReason>();
            EvaluatedAtUtc = evaluatedAtUtc;
            PolicyVersion = policyVersion;
        }

        public static WorkstationHealthEvaluationResult CreateHealthy(
            WorkstationIdentity identity,
            DateTime evaluatedAtUtc,
            string? policyVersion = null)
        {
            return new WorkstationHealthEvaluationResult(
                identity,
                WorkstationHealthState.Healthy,
                100.0,
                new List<WorkstationHealthReason>(),
                evaluatedAtUtc,
                policyVersion);
        }

        public static WorkstationHealthEvaluationResult CreateUnknown(
            WorkstationIdentity identity,
            string message,
            DateTime evaluatedAtUtc,
            string? policyVersion = null)
        {
            var reasons = new List<WorkstationHealthReason>();
            if (!string.IsNullOrWhiteSpace(message))
            {
                reasons.Add(new WorkstationHealthReason(
                    "INSUFFICIENT_DATA",
                    WorkstationHealthState.Unknown,
                    "State",
                    0,
                    0,
                    message,
                    evaluatedAtUtc,
                    policyVersion));
            }

            return new WorkstationHealthEvaluationResult(
                identity,
                WorkstationHealthState.Unknown,
                50.0,
                reasons,
                evaluatedAtUtc,
                policyVersion);
        }

        public static WorkstationHealthEvaluationResult CreateOffline(
            WorkstationIdentity identity,
            string message,
            DateTime evaluatedAtUtc,
            string? policyVersion = null)
        {
            var reasons = new List<WorkstationHealthReason>
            {
                new WorkstationHealthReason(
                    WorkstationHealthReasonCode.ConnectionLost,
                    WorkstationHealthState.Offline,
                    "Transport",
                    0,
                    0,
                    message,
                    evaluatedAtUtc,
                    policyVersion)
            };

            return new WorkstationHealthEvaluationResult(
                identity,
                WorkstationHealthState.Offline,
                0.0,
                reasons,
                evaluatedAtUtc,
                policyVersion);
        }
    }
}
