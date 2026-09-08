using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Telemetry;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Infrastructure.Telemetry
{
    public sealed class AlertNotificationDispatcher : IAlertNotificationDispatcher
    {
        private readonly ISecurityEventService? _securityEventService;
        private readonly ILogger<AlertNotificationDispatcher> _logger;

        public AlertNotificationDispatcher(
            ILogger<AlertNotificationDispatcher> logger,
            ISecurityEventService? securityEventService = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _securityEventService = securityEventService;
        }

        public async Task DispatchNotificationAsync(Incident incident, string notificationType, CancellationToken cancellationToken = default)
        {
            if (incident == null) return;

            try
            {
                _logger.LogInformation(
                    "Alert Notification [{NotificationType}] for Incident {IncidentId} (Rule: {RuleCode}, PcId: {PcId}, Severity: {Severity}, State: {State}). Title: {Title}",
                    notificationType, incident.Id, incident.RuleCode, incident.PcId, incident.Severity, incident.LifecycleState, incident.Title);

                if (incident.RuleCode == "SECURITY_EVENT_FLAGGED" && _securityEventService != null)
                {
                    await _securityEventService.RecordSecurityEventAsync(
                        eventType: $"ALERT_NOTIFICATION_{notificationType}",
                        actorId: null,
                        actorType: "WORKSTATION",
                        deviceId: incident.PcId,
                        organizationId: incident.OrganizationId,
                        siteId: incident.SiteId,
                        resourceType: "INCIDENT",
                        resourceId: incident.Id,
                        action: notificationType,
                        result: "DISPATCHED",
                        failureReason: null,
                        correlationId: incident.Fingerprint,
                        cancellationToken: cancellationToken);
                }
            }
            catch (Exception ex)
            {
                // Delivery failures must not crash or corrupt durable incident state
                _logger.LogWarning(ex, "Failed to dispatch alert notification for incident {IncidentId}.", incident.Id);
            }
        }
    }
}
