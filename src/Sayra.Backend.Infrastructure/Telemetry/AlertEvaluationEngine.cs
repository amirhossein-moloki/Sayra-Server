using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Telemetry;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Domain.Enums;
using Sayra.Backend.Domain.Telemetry;

namespace Sayra.Backend.Infrastructure.Telemetry
{
    public sealed class AlertEvaluationEngine : IAlertEvaluationEngine
    {
        private readonly IIncidentRepository _incidentRepository;
        private readonly IOptions<AlertingOptions> _options;
        private readonly IAlertMetrics _metrics;
        private readonly IAlertNotificationDispatcher _dispatcher;
        private readonly ISecurityEventService? _securityEventService;
        private readonly ILogger<AlertEvaluationEngine> _logger;

        public AlertEvaluationEngine(
            IIncidentRepository incidentRepository,
            IOptions<AlertingOptions> options,
            IAlertMetrics metrics,
            IAlertNotificationDispatcher dispatcher,
            ILogger<AlertEvaluationEngine> logger,
            ISecurityEventService? securityEventService = null)
        {
            _incidentRepository = incidentRepository ?? throw new ArgumentNullException(nameof(incidentRepository));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _securityEventService = securityEventService;
        }

        public string CalculateFingerprint(Guid organizationId, Guid? siteId, string pcId, string ruleCode, string? resource = null)
        {
            if (string.IsNullOrWhiteSpace(pcId)) throw new ArgumentNullException(nameof(pcId));
            if (string.IsNullOrWhiteSpace(ruleCode)) throw new ArgumentNullException(nameof(ruleCode));

            string raw = $"{organizationId}:{siteId?.ToString() ?? "GLOBAL"}:{pcId.Trim().ToUpperInvariant()}:{ruleCode.Trim().ToUpperInvariant()}:{resource?.Trim().ToUpperInvariant() ?? "WORKSTATION"}";
            using var sha256 = SHA256.Create();
            byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(raw));
            return Convert.ToHexString(hashBytes);
        }

        public async Task<IReadOnlyList<Incident>> EvaluateHealthResultAsync(WorkstationHealthEvaluationResult healthResult, CancellationToken cancellationToken = default)
        {
            if (healthResult == null || healthResult.Identity == null)
            {
                return Array.Empty<Incident>();
            }

            if (!_options.Value.IsEnabled)
            {
                return Array.Empty<Incident>();
            }

            var sw = Stopwatch.StartNew();
            var evaluatedIncidents = new List<Incident>();

            try
            {
                string pcId = healthResult.Identity.PcId;
                Guid orgId = healthResult.Identity.OrganizationId ?? Guid.Empty;
                Guid? siteId = healthResult.Identity.SiteId;
                Guid? workstationId = healthResult.Identity.WorkstationId;

                var rules = _options.Value.Rules ?? AlertingOptions.GetDefaultRules();
                var activeReasons = healthResult.Reasons ?? new List<WorkstationHealthReason>();

                var existingActiveIncidents = await _incidentRepository.GetActiveIncidentsForWorkstationAsync(pcId, cancellationToken);
                var activeDict = existingActiveIncidents.ToDictionary(i => i.Fingerprint, StringComparer.Ordinal);
                var processedFingerprints = new HashSet<string>(StringComparer.Ordinal);

                foreach (var reason in activeReasons)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string ruleCode = reason.ReasonCode;
                    var rule = rules.FirstOrDefault(r => string.Equals(r.RuleCode, ruleCode, StringComparison.OrdinalIgnoreCase))
                               ?? new AlertRule(ruleCode, ruleCode, reason.Message, MapStateToSeverity(reason.Severity));

                    if (!rule.IsEnabled)
                    {
                        continue;
                    }

                    string fingerprint = CalculateFingerprint(orgId, siteId, pcId, ruleCode, reason.Source);
                    processedFingerprints.Add(fingerprint);

                    AlertSeverity severity = MapStateToSeverity(reason.Severity);
                    if ((int)rule.Severity > (int)severity)
                    {
                        severity = rule.Severity;
                    }

                    _metrics.RecordAlertEvaluated(ruleCode, severity.ToString());

                    string evidenceJson = $"{{\"observedValue\":{reason.ObservedValue},\"threshold\":{reason.Threshold},\"message\":\"{EscapeJson(reason.Message)}\",\"healthState\":\"{healthResult.HealthState}\"}}";

                    if (activeDict.TryGetValue(fingerprint, out var existingIncident))
                    {
                        existingIncident.RecordObservation(
                            evidenceJson,
                            healthResult.EvaluatedAtUtc,
                            TimeSpan.FromSeconds(rule.MinSustainedSeconds),
                            severity);

                        await _incidentRepository.SaveIncidentAsync(existingIncident, cancellationToken);
                        _metrics.RecordIncidentDeduplicated(ruleCode);
                        evaluatedIncidents.Add(existingIncident);
                    }
                    else
                    {
                        bool isSuppressed = false;
                        string? suppressionReason = null;

                        var newIncident = Incident.CreateTriggered(
                            fingerprint,
                            ruleCode,
                            orgId,
                            siteId,
                            workstationId,
                            pcId,
                            severity,
                            reason.ReasonCode,
                            reason.Source,
                            $"{rule.Name} on {pcId}",
                            reason.Message,
                            evidenceJson,
                            healthResult.EvaluatedAtUtc,
                            healthResult.PolicyVersion,
                            isSuppressed,
                            suppressionReason);

                        if (rule.MinSustainedSeconds <= 0)
                        {
                            newIncident.TransitionToFiring(healthResult.EvaluatedAtUtc);
                        }

                        await _incidentRepository.SaveIncidentAsync(newIncident, cancellationToken);

                        _metrics.RecordAlertTriggered(ruleCode, severity.ToString());
                        _metrics.RecordIncidentCreated(ruleCode, severity.ToString());

                        string notificationState = newIncident.LifecycleState == IncidentLifecycleState.Firing ? "FIRING" : "TRIGGERED";
                        await _dispatcher.DispatchNotificationAsync(newIncident, notificationState, cancellationToken);

                        evaluatedIncidents.Add(newIncident);
                    }
                }

                foreach (var activeIncident in activeDict.Values)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!processedFingerprints.Contains(activeIncident.Fingerprint))
                    {
                        string recoveryEvidence = $"{{\"recoveredAt\":\"{healthResult.EvaluatedAtUtc:o}\",\"healthState\":\"{healthResult.HealthState}\",\"message\":\"Condition resolved according to workstation health evaluation\"}}";

                        activeIncident.Resolve(recoveryEvidence, healthResult.EvaluatedAtUtc);
                        await _incidentRepository.SaveIncidentAsync(activeIncident, cancellationToken);

                        _metrics.RecordIncidentResolved(activeIncident.RuleCode);
                        await _dispatcher.DispatchNotificationAsync(activeIncident, "RESOLVED", cancellationToken);

                        evaluatedIncidents.Add(activeIncident);
                    }
                }

                return evaluatedIncidents;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _metrics.RecordEvaluationFailure();
                _logger.LogError(ex, "Error evaluating alerts for workstation health result (PcId: {PcId}).", healthResult.Identity?.PcId);
                throw;
            }
            finally
            {
                sw.Stop();
                _metrics.RecordEvaluationDuration(sw.Elapsed.TotalSeconds);
            }
        }

        public async Task<Incident?> EvaluateOperationalEventAsync(OperationalEventSignal eventSignal, CancellationToken cancellationToken = default)
        {
            if (eventSignal == null || eventSignal.Identity == null || !_options.Value.IsEnabled)
            {
                return null;
            }

            string ruleCode = eventSignal.EventType.ToUpperInvariant();
            var rules = _options.Value.Rules ?? AlertingOptions.GetDefaultRules();
            var rule = rules.FirstOrDefault(r => string.Equals(r.RuleCode, ruleCode, StringComparison.OrdinalIgnoreCase));

            if (rule != null && !rule.IsEnabled)
            {
                return null;
            }

            AlertSeverity severity = rule?.Severity ?? AlertSeverity.Warning;
            string pcId = eventSignal.Identity.PcId;
            Guid orgId = eventSignal.Identity.OrganizationId ?? Guid.Empty;
            Guid? siteId = eventSignal.Identity.SiteId;

            string fingerprint = CalculateFingerprint(orgId, siteId, pcId, ruleCode, "EVENT");
            var activeIncident = await _incidentRepository.GetActiveIncidentByFingerprintAsync(fingerprint, cancellationToken);

            string evidenceJson = $"{{\"eventPayload\":\"{EscapeJson(eventSignal.Payload ?? string.Empty)}\",\"eventId\":\"{eventSignal.EventId}\",\"timestamp\":\"{eventSignal.OccurredAt:o}\"}}";

            if (activeIncident != null)
            {
                activeIncident.RecordObservation(evidenceJson, eventSignal.ServerReceivedAt, TimeSpan.Zero, severity);
                await _incidentRepository.SaveIncidentAsync(activeIncident, cancellationToken);
                _metrics.RecordIncidentDeduplicated(ruleCode);
                return activeIncident;
            }
            else
            {
                var newIncident = Incident.CreateFiring(
                    fingerprint,
                    ruleCode,
                    orgId,
                    siteId,
                    eventSignal.Identity.WorkstationId,
                    pcId,
                    severity,
                    ruleCode,
                    "EVENT",
                    $"{eventSignal.EventType} on {pcId}",
                    eventSignal.Payload ?? eventSignal.EventType,
                    evidenceJson,
                    eventSignal.ServerReceivedAt);

                await _incidentRepository.SaveIncidentAsync(newIncident, cancellationToken);
                _metrics.RecordAlertTriggered(ruleCode, severity.ToString());
                _metrics.RecordIncidentCreated(ruleCode, severity.ToString());

                await _dispatcher.DispatchNotificationAsync(newIncident, "FIRING", cancellationToken);
                return newIncident;
            }
        }

        private static AlertSeverity MapStateToSeverity(WorkstationHealthState state)
        {
            return state switch
            {
                WorkstationHealthState.Healthy => AlertSeverity.Info,
                WorkstationHealthState.Warning => AlertSeverity.Warning,
                WorkstationHealthState.Degraded => AlertSeverity.Error,
                WorkstationHealthState.Critical => AlertSeverity.Critical,
                WorkstationHealthState.Offline => AlertSeverity.Critical,
                _ => AlertSeverity.Warning
            };
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
        }
    }
}
