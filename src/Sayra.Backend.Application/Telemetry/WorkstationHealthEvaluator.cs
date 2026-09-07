using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Domain.Enums;
using Sayra.Backend.Domain.Telemetry;

namespace Sayra.Backend.Application.Telemetry
{
    public sealed class WorkstationHealthEvaluator : IWorkstationHealthEvaluator
    {
        private readonly WorkstationHealthPolicyOptions _defaultOptions;
        private readonly ITelemetryAggregateRepository? _aggregateRepository;

        public WorkstationHealthEvaluator(
            IOptions<WorkstationHealthPolicyOptions> options,
            ITelemetryAggregateRepository? aggregateRepository = null)
        {
            _defaultOptions = options?.Value ?? new WorkstationHealthPolicyOptions();
            _aggregateRepository = aggregateRepository;
        }

        public async Task<WorkstationHealthEvaluationResult> EvaluateWorkstationHealthAsync(
            WorkstationRealTimeState? currentState,
            WorkstationHealthEvaluationResult? previousResult = null,
            WorkstationHealthPolicyOptions? customPolicy = null,
            CancellationToken cancellationToken = default)
        {
            var policy = customPolicy ?? _defaultOptions;
            var nowUtc = DateTime.UtcNow;
            const string policyVersion = "1.0";

            if (currentState == null)
            {
                var unknownIdentity = previousResult?.Identity ?? new Domain.ValueObjects.WorkstationIdentity("UNKNOWN");
                return WorkstationHealthEvaluationResult.CreateUnknown(
                    unknownIdentity,
                    "Workstation real-time state unavailable",
                    nowUtc,
                    policyVersion);
            }

            var identity = currentState.GetIdentity();

            // Evaluate fundamental operational state
            var opState = currentState.EvaluateOperationalState(nowUtc, policy.TelemetryStaleThreshold, policy.OfflineTimeout);

            if (opState == WorkstationOperationalState.Offline)
            {
                return WorkstationHealthEvaluationResult.CreateOffline(
                    identity,
                    $"Workstation disconnected and last seen {(currentState.LastSeenAt.HasValue ? (nowUtc - currentState.LastSeenAt.Value).TotalSeconds : 0):F0}s ago",
                    nowUtc,
                    policyVersion);
            }

            if (opState == WorkstationOperationalState.Unknown)
            {
                return WorkstationHealthEvaluationResult.CreateUnknown(
                    identity,
                    "Workstation has no connectivity or telemetry activity recorded",
                    nowUtc,
                    policyVersion);
            }

            var reasons = new List<WorkstationHealthReason>();

            // Query Stage 08-05 aggregates for recent window metrics if available
            double? windowCpuAvg = null;
            double? windowRamAvg = null;
            if (_aggregateRepository != null && identity.WorkstationId.HasValue && identity.WorkstationId.Value != Guid.Empty)
            {
                try
                {
                    var windowStart = nowUtc.AddSeconds(-policy.CpuSustainedDurationSeconds);
                    var aggregates = await _aggregateRepository.GetAggregatesForWorkstationAsync(
                        identity.WorkstationId.Value,
                        "1m",
                        windowStart,
                        nowUtc,
                        10,
                        cancellationToken);

                    if (aggregates != null && aggregates.Count > 0)
                    {
                        windowCpuAvg = aggregates.Average(a => a.CpuAvg);
                        windowRamAvg = aggregates.Average(a => a.RamAvg);
                    }
                }
                catch
                {
                    // Fail-safe fallback to current real-time state metrics
                }
            }

            // Signal 1: Telemetry Freshness
            if (currentState.IsConnected && !currentState.IsTelemetryFresh(nowUtc, policy.TelemetryStaleThreshold))
            {
                var staleSeconds = currentState.LastTelemetryReceivedAt.HasValue
                    ? (nowUtc - currentState.LastTelemetryReceivedAt.Value).TotalSeconds
                    : policy.TelemetryStaleThresholdSeconds;

                var severity = staleSeconds >= (policy.TelemetryStaleThresholdSeconds * 2)
                    ? WorkstationHealthState.Degraded
                    : WorkstationHealthState.Warning;

                var prevReason = GetPreviousReason(previousResult, WorkstationHealthReasonCode.TelemetryStale);
                reasons.Add(new WorkstationHealthReason(
                    WorkstationHealthReasonCode.TelemetryStale,
                    severity,
                    "Telemetry",
                    staleSeconds,
                    policy.TelemetryStaleThresholdSeconds,
                    $"Telemetry reports are stale (last received {staleSeconds:F0}s ago)",
                    prevReason?.FirstDetectedAtUtc ?? nowUtc,
                    policyVersion));
            }

            // Signal 2: Heartbeat Freshness
            if (currentState.IsConnected && !currentState.IsHeartbeatFresh(nowUtc, policy.HeartbeatTimeout))
            {
                var heartbeatStaleSeconds = currentState.LastHeartbeatReceivedAt.HasValue
                    ? (nowUtc - currentState.LastHeartbeatReceivedAt.Value).TotalSeconds
                    : policy.HeartbeatTimeoutSeconds;

                var prevReason = GetPreviousReason(previousResult, WorkstationHealthReasonCode.HeartbeatTimeout);
                reasons.Add(new WorkstationHealthReason(
                    WorkstationHealthReasonCode.HeartbeatTimeout,
                    WorkstationHealthState.Warning,
                    "Transport",
                    heartbeatStaleSeconds,
                    policy.HeartbeatTimeoutSeconds,
                    $"Heartbeat interval exceeded (last received {heartbeatStaleSeconds:F0}s ago)",
                    prevReason?.FirstDetectedAtUtc ?? nowUtc,
                    policyVersion));
            }

            // Signal 3: CPU Usage with Duration, Aggregates & Hysteresis
            if (currentState.Cpu.HasValue || windowCpuAvg.HasValue)
            {
                var cpuVal = windowCpuAvg ?? currentState.Cpu!.Value;
                var prevCpuReason = GetPreviousReason(previousResult, WorkstationHealthReasonCode.CpuSustainedHigh);

                // Use sustained duration check if previous result exists
                bool isSustainedDurationMet = prevCpuReason == null || (nowUtc - prevCpuReason.FirstDetectedAtUtc).TotalSeconds >= policy.CpuSustainedDurationSeconds || windowCpuAvg.HasValue;

                bool isHighCpu = cpuVal >= policy.CpuWarningThresholdPercent;
                bool isRecovery = prevCpuReason != null && cpuVal < policy.CpuRecoveryThresholdPercent;
                bool retainHysteresis = prevCpuReason != null && !isRecovery;

                if (isHighCpu || retainHysteresis)
                {
                    var severity = (cpuVal >= policy.CpuCriticalThresholdPercent && isSustainedDurationMet) || (prevCpuReason?.Severity == WorkstationHealthState.Critical && retainHysteresis)
                        ? WorkstationHealthState.Critical
                        : WorkstationHealthState.Warning;

                    var threshold = severity == WorkstationHealthState.Critical
                        ? policy.CpuCriticalThresholdPercent
                        : policy.CpuWarningThresholdPercent;

                    reasons.Add(new WorkstationHealthReason(
                        WorkstationHealthReasonCode.CpuSustainedHigh,
                        severity,
                        "System.CPU",
                        cpuVal,
                        threshold,
                        $"CPU utilization is sustained high at {cpuVal:F1}% (threshold {threshold}%)",
                        prevCpuReason?.FirstDetectedAtUtc ?? nowUtc,
                        policyVersion));
                }
            }

            // Signal 4: RAM Usage with Duration, Aggregates & Hysteresis
            if (currentState.Ram.HasValue || windowRamAvg.HasValue)
            {
                var ramVal = windowRamAvg ?? currentState.Ram!.Value;
                var prevRamReason = GetPreviousReason(previousResult, WorkstationHealthReasonCode.MemorySustainedHigh);

                bool isSustainedDurationMet = prevRamReason == null || (nowUtc - prevRamReason.FirstDetectedAtUtc).TotalSeconds >= policy.RamSustainedDurationSeconds || windowRamAvg.HasValue;

                bool isHighRam = ramVal >= policy.RamWarningThresholdPercent;
                bool isRecovery = prevRamReason != null && ramVal < policy.RamRecoveryThresholdPercent;
                bool retainHysteresis = prevRamReason != null && !isRecovery;

                if (isHighRam || retainHysteresis)
                {
                    var severity = (ramVal >= policy.RamCriticalThresholdPercent && isSustainedDurationMet) || (prevRamReason?.Severity == WorkstationHealthState.Critical && retainHysteresis)
                        ? WorkstationHealthState.Critical
                        : WorkstationHealthState.Warning;

                    var threshold = severity == WorkstationHealthState.Critical
                        ? policy.RamCriticalThresholdPercent
                        : policy.RamWarningThresholdPercent;

                    reasons.Add(new WorkstationHealthReason(
                        WorkstationHealthReasonCode.MemorySustainedHigh,
                        severity,
                        "System.RAM",
                        ramVal,
                        threshold,
                        $"RAM utilization is sustained high at {ramVal:F1}% (threshold {threshold}%)",
                        prevRamReason?.FirstDetectedAtUtc ?? nowUtc,
                        policyVersion));
                }
            }

            // Signal 5: Operational Events & Crashes
            if (currentState.TotalCrashes.HasValue && currentState.TotalCrashes.Value > 0)
            {
                var crashes = currentState.TotalCrashes.Value;
                if (crashes >= policy.GameCrashWarningThresholdCount)
                {
                    var severity = crashes >= policy.GameCrashCriticalThresholdCount
                        ? WorkstationHealthState.Critical
                        : WorkstationHealthState.Warning;

                    var threshold = severity == WorkstationHealthState.Critical
                        ? policy.GameCrashCriticalThresholdCount
                        : policy.GameCrashWarningThresholdCount;

                    var prevCrashReason = GetPreviousReason(previousResult, WorkstationHealthReasonCode.GameCrashFrequencyHigh);

                    reasons.Add(new WorkstationHealthReason(
                        WorkstationHealthReasonCode.GameCrashFrequencyHigh,
                        severity,
                        "Application",
                        crashes,
                        threshold,
                        $"Application/Game crash counter elevated ({crashes} crashes observed)",
                        prevCrashReason?.FirstDetectedAtUtc ?? nowUtc,
                        policyVersion));
                }
            }

            // Signal 6: Software Update State
            if (!string.IsNullOrWhiteSpace(currentState.UpdateState))
            {
                var updateStateUpper = currentState.UpdateState.ToUpperInvariant();
                if (updateStateUpper.Contains("FAILED") || updateStateUpper.Contains("REVOKED") || updateStateUpper.Contains("QUARANTINED"))
                {
                    var prevUpdateReason = GetPreviousReason(previousResult, WorkstationHealthReasonCode.UpdateFailed);
                    reasons.Add(new WorkstationHealthReason(
                        WorkstationHealthReasonCode.UpdateFailed,
                        WorkstationHealthState.Degraded,
                        "Updates",
                        1,
                        0,
                        $"Software update reported failed state: {currentState.UpdateState}",
                        prevUpdateReason?.FirstDetectedAtUtc ?? nowUtc,
                        policyVersion));
                }
            }

            // Signal 7: Configuration Sync Failures
            if (!string.IsNullOrWhiteSpace(currentState.LastEventType))
            {
                if (currentState.LastEventType.Equals("CONFIG_SYNC_FAILED", StringComparison.OrdinalIgnoreCase))
                {
                    var prevConfigReason = GetPreviousReason(previousResult, WorkstationHealthReasonCode.ConfigSyncFailed);
                    reasons.Add(new WorkstationHealthReason(
                        WorkstationHealthReasonCode.ConfigSyncFailed,
                        WorkstationHealthState.Warning,
                        "Configuration",
                        1,
                        0,
                        "Configuration synchronization failure reported",
                        prevConfigReason?.FirstDetectedAtUtc ?? nowUtc,
                        policyVersion));
                }
            }

            // Compute Final Health State based on highest severity reason
            var finalState = WorkstationHealthState.Healthy;
            if (reasons.Count > 0)
            {
                if (reasons.Any(r => r.Severity == WorkstationHealthState.Critical))
                {
                    finalState = WorkstationHealthState.Critical;
                }
                else if (reasons.Any(r => r.Severity == WorkstationHealthState.Degraded))
                {
                    finalState = WorkstationHealthState.Degraded;
                }
                else if (reasons.Any(r => r.Severity == WorkstationHealthState.Warning))
                {
                    finalState = WorkstationHealthState.Warning;
                }
            }

            // Calculate Explainable Health Score (100.0 base)
            double score = 100.0;
            foreach (var r in reasons)
            {
                switch (r.Severity)
                {
                    case WorkstationHealthState.Critical:
                        score -= 40.0;
                        break;
                    case WorkstationHealthState.Degraded:
                        score -= 25.0;
                        break;
                    case WorkstationHealthState.Warning:
                        score -= 10.0;
                        break;
                }
            }

            score = Math.Max(0.0, Math.Min(100.0, score));

            return new WorkstationHealthEvaluationResult(
                identity,
                finalState,
                score,
                reasons,
                nowUtc,
                policyVersion);
        }

        private static WorkstationHealthReason? GetPreviousReason(
            WorkstationHealthEvaluationResult? previousResult,
            string reasonCode)
        {
            if (previousResult?.Reasons == null) return null;
            return previousResult.Reasons.FirstOrDefault(r => string.Equals(r.ReasonCode, reasonCode, StringComparison.OrdinalIgnoreCase));
        }
    }
}
