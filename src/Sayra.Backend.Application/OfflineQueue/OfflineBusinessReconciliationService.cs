using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Sayra.Backend.Application.Abstractions.Communication;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Sessions;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Application.OfflineQueue
{
    public class OfflineBusinessReconciliationService : IOfflineBusinessReconciliationService
    {
        private readonly ICommandHandler<StartSessionCommand, SessionResponseDto> _startSessionHandler;
        private readonly ICommandHandler<StopSessionCommand, SessionResponseDto> _stopSessionHandler;
        private readonly ICommandHandler<PauseSessionCommand, SessionResponseDto> _pauseSessionHandler;
        private readonly ICommandHandler<ResumeSessionCommand, SessionResponseDto> _resumeSessionHandler;
        private readonly ICommandHandler<ExtendSessionCommand, SessionExtensionResponseDto> _extendSessionHandler;
        private readonly IRemoteCommandManager? _remoteCommandManager;
        private readonly IRepository<AuditEvent> _auditEventRepository;
        private readonly ILogger<OfflineBusinessReconciliationService> _logger;
        private readonly IOfflineMetrics? _metrics;

        public OfflineBusinessReconciliationService(
            ICommandHandler<StartSessionCommand, SessionResponseDto> startSessionHandler,
            ICommandHandler<StopSessionCommand, SessionResponseDto> stopSessionHandler,
            ICommandHandler<PauseSessionCommand, SessionResponseDto> pauseSessionHandler,
            ICommandHandler<ResumeSessionCommand, SessionResponseDto> resumeSessionHandler,
            ICommandHandler<ExtendSessionCommand, SessionExtensionResponseDto> extendSessionHandler,
            IRepository<AuditEvent> auditEventRepository,
            ILogger<OfflineBusinessReconciliationService> logger,
            IRemoteCommandManager? remoteCommandManager = null,
            IOfflineMetrics? metrics = null)
        {
            _startSessionHandler = startSessionHandler ?? throw new ArgumentNullException(nameof(startSessionHandler));
            _stopSessionHandler = stopSessionHandler ?? throw new ArgumentNullException(nameof(stopSessionHandler));
            _pauseSessionHandler = pauseSessionHandler ?? throw new ArgumentNullException(nameof(pauseSessionHandler));
            _resumeSessionHandler = resumeSessionHandler ?? throw new ArgumentNullException(nameof(resumeSessionHandler));
            _extendSessionHandler = extendSessionHandler ?? throw new ArgumentNullException(nameof(extendSessionHandler));
            _auditEventRepository = auditEventRepository ?? throw new ArgumentNullException(nameof(auditEventRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _remoteCommandManager = remoteCommandManager;
            _metrics = metrics;
        }

        public async Task<BusinessReconciliationResult> ReconcileAsync(
            OfflineQueueItem item,
            Workstation workstation,
            Site? site,
            string batchId,
            CancellationToken cancellationToken = default)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (workstation == null) throw new ArgumentNullException(nameof(workstation));

            string eventType = item.EventType?.Trim().ToUpperInvariant() ?? string.Empty;
            string payloadText = item.Payload is JsonElement elem ? elem.GetRawText() : (item.Payload?.ToString() ?? "{}");

            _logger.LogInformation("Reconciling offline event {EventId} of type {EventType} for workstation {PcId}.",
                item.EventId, eventType, workstation.PcId);

            var result = eventType switch
            {
                "SESSION_COMMAND_REQUEST" => await ReconcileSessionCommandRequestAsync(item, workstation, payloadText, cancellationToken),
                ClientEventType.SessionRuntimeEvent => await ReconcileInformationalEventAsync(item, workstation, "SESSION_RUNTIME_EVENT", payloadText, cancellationToken),
                ClientEventType.SecurityEvent => await ReconcileInformationalEventAsync(item, workstation, "SECURITY_EVENT", payloadText, cancellationToken),
                ClientEventType.ClientStarted => await ReconcileInformationalEventAsync(item, workstation, "CLIENT_STARTED", payloadText, cancellationToken),
                ClientEventType.ClientStopped => await ReconcileInformationalEventAsync(item, workstation, "CLIENT_STOPPED", payloadText, cancellationToken),
                ClientEventType.WorkstationStateChanged => await ReconcileInformationalEventAsync(item, workstation, "WORKSTATION_STATE_CHANGED", payloadText, cancellationToken),
                ClientEventType.ApplicationStarted => await ReconcileInformationalEventAsync(item, workstation, "APPLICATION_STARTED", payloadText, cancellationToken),
                ClientEventType.ApplicationExited => await ReconcileInformationalEventAsync(item, workstation, "APPLICATION_EXITED", payloadText, cancellationToken),
                ClientEventType.ApplicationCrashed => await ReconcileInformationalEventAsync(item, workstation, "APPLICATION_CRASHED", payloadText, cancellationToken),
                ClientEventType.ConfigurationChanged => await ReconcileInformationalEventAsync(item, workstation, "CONFIGURATION_CHANGED", payloadText, cancellationToken),
                ClientEventType.NetworkChanged => await ReconcileInformationalEventAsync(item, workstation, "NETWORK_CHANGED", payloadText, cancellationToken),
                ClientEventType.DeviceChanged => await ReconcileInformationalEventAsync(item, workstation, "DEVICE_CHANGED", payloadText, cancellationToken),
                ClientEventType.DiagnosticEvent => await ReconcileInformationalEventAsync(item, workstation, "DIAGNOSTIC_EVENT", payloadText, cancellationToken),
                "COMMAND_ACK" => await ReconcileCommandAckAsync(item, workstation, payloadText, cancellationToken),
                "EXECUTION_RESULT" or "COMMAND_RESULT" => await ReconcileExecutionResultAsync(item, workstation, payloadText, cancellationToken),
                _ => ReconcileUnsupportedEvent(item, eventType)
            };

            if (result.Status == OfflineReconciliationStatus.Conflict)
            {
                _metrics?.RecordReconciliationConflict(eventType, result.ReasonCode);
            }

            return result;
        }

        private async Task<BusinessReconciliationResult> ReconcileSessionCommandRequestAsync(
            OfflineQueueItem item,
            Workstation workstation,
            string payloadText,
            CancellationToken cancellationToken)
        {
            SessionCommandPayload? cmdPayload;
            try
            {
                cmdPayload = JsonSerializer.Deserialize<SessionCommandPayload>(payloadText, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize SESSION_COMMAND_REQUEST payload for event {EventId}.", item.EventId);
                return BusinessReconciliationResult.Reject(OfflineReasonCode.MalformedPayload, "Invalid JSON payload for SESSION_COMMAND_REQUEST.");
            }

            if (cmdPayload == null || string.IsNullOrWhiteSpace(cmdPayload.Action))
            {
                return BusinessReconciliationResult.Reject(OfflineReasonCode.MalformedPayload, "SESSION_COMMAND_REQUEST action cannot be null or empty.");
            }

            // Workstation Identity Boundary Check: If payload specifies workstationId, it must match authenticated workstation
            if (cmdPayload.WorkstationId != Guid.Empty && cmdPayload.WorkstationId != workstation.Id)
            {
                _logger.LogWarning("SECURITY ALERT: Session command payload workstation {PayloadWsId} does not match authenticated workstation {AuthWsId}.",
                    cmdPayload.WorkstationId, workstation.Id);
                return BusinessReconciliationResult.Reject(OfflineReasonCode.IdentityMismatch, "Payload workstation identity does not match authenticated workstation identity.");
            }

            string action = cmdPayload.Action.Trim().ToUpperInvariant();

            switch (action)
            {
                case "START":
                    {
                        if (cmdPayload.GamerId == Guid.Empty)
                        {
                            return BusinessReconciliationResult.Reject(OfflineReasonCode.MalformedPayload, "GamerId is required for START session command.");
                        }

                        var startCmd = new StartSessionCommand(cmdPayload.GamerId, workstation.Id, cmdPayload.ReservationId);
                        var startRes = await _startSessionHandler.HandleAsync(startCmd, cancellationToken);

                        if (startRes.IsSuccess)
                        {
                            _logger.LogInformation("Successfully reconciled offline START session command for workstation {PcId}. SessionId: {SessionId}.",
                                workstation.PcId, startRes.Value!.SessionId);
                            return BusinessReconciliationResult.Success("SUCCESS_SESSION_STARTED", startRes.Value.SessionId, startRes.Value);
                        }

                        _logger.LogWarning("Offline START session reconciliation failed for workstation {PcId}: [{Code}] {Message}",
                            workstation.PcId, startRes.ErrorCode, startRes.ErrorMessage);

                        return BusinessReconciliationResult.Conflict("SESSION_START_FAILED", startRes.ErrorMessage ?? "Failed to start session.");
                    }

                case "PAUSE":
                    {
                        if (cmdPayload.SessionId == Guid.Empty)
                        {
                            return BusinessReconciliationResult.Reject(OfflineReasonCode.MalformedPayload, "SessionId is required for PAUSE session command.");
                        }

                        var pauseCmd = new PauseSessionCommand(cmdPayload.SessionId);
                        var pauseRes = await _pauseSessionHandler.HandleAsync(pauseCmd, cancellationToken);

                        if (pauseRes.IsSuccess)
                        {
                            _logger.LogInformation("Successfully reconciled offline PAUSE session command for SessionId {SessionId}.", cmdPayload.SessionId);
                            return BusinessReconciliationResult.Success("SUCCESS_SESSION_PAUSED", pauseRes.Value!.SessionId, pauseRes.Value);
                        }

                        _logger.LogWarning("Offline PAUSE session reconciliation failed for SessionId {SessionId}: [{Code}] {Message}",
                            cmdPayload.SessionId, pauseRes.ErrorCode, pauseRes.ErrorMessage);

                        return BusinessReconciliationResult.Conflict("SESSION_PAUSE_FAILED", pauseRes.ErrorMessage ?? "Failed to pause session.");
                    }

                case "RESUME":
                    {
                        if (cmdPayload.SessionId == Guid.Empty)
                        {
                            return BusinessReconciliationResult.Reject(OfflineReasonCode.MalformedPayload, "SessionId is required for RESUME session command.");
                        }

                        var resumeCmd = new ResumeSessionCommand(cmdPayload.SessionId);
                        var resumeRes = await _resumeSessionHandler.HandleAsync(resumeCmd, cancellationToken);

                        if (resumeRes.IsSuccess)
                        {
                            _logger.LogInformation("Successfully reconciled offline RESUME session command for SessionId {SessionId}.", cmdPayload.SessionId);
                            return BusinessReconciliationResult.Success("SUCCESS_SESSION_RESUMED", resumeRes.Value!.SessionId, resumeRes.Value);
                        }

                        _logger.LogWarning("Offline RESUME session reconciliation failed for SessionId {SessionId}: [{Code}] {Message}",
                            cmdPayload.SessionId, resumeRes.ErrorCode, resumeRes.ErrorMessage);

                        return BusinessReconciliationResult.Conflict("SESSION_RESUME_FAILED", resumeRes.ErrorMessage ?? "Failed to resume session.");
                    }

                case "STOP":
                    {
                        if (cmdPayload.SessionId == Guid.Empty)
                        {
                            return BusinessReconciliationResult.Reject(OfflineReasonCode.MalformedPayload, "SessionId is required for STOP session command.");
                        }

                        var stopCmd = new StopSessionCommand(cmdPayload.SessionId);
                        var stopRes = await _stopSessionHandler.HandleAsync(stopCmd, cancellationToken);

                        if (stopRes.IsSuccess)
                        {
                            _logger.LogInformation("Successfully reconciled offline STOP session command for SessionId {SessionId}.", cmdPayload.SessionId);
                            return BusinessReconciliationResult.Success("SUCCESS_SESSION_STOPPED", stopRes.Value!.SessionId, stopRes.Value);
                        }

                        _logger.LogWarning("Offline STOP session reconciliation failed for SessionId {SessionId}: [{Code}] {Message}",
                            cmdPayload.SessionId, stopRes.ErrorCode, stopRes.ErrorMessage);

                        return BusinessReconciliationResult.Conflict("SESSION_STOP_FAILED", stopRes.ErrorMessage ?? "Failed to stop session.");
                    }

                case "EXTEND":
                    {
                        if (cmdPayload.SessionId == Guid.Empty)
                        {
                            return BusinessReconciliationResult.Reject(OfflineReasonCode.MalformedPayload, "SessionId is required for EXTEND session command.");
                        }

                        int additionalMinutes = cmdPayload.AdditionalMinutes ?? 30;
                        string idempotencyKey = !string.IsNullOrWhiteSpace(cmdPayload.IdempotencyKey)
                            ? cmdPayload.IdempotencyKey
                            : $"OFFLINE-EXT-{item.EventId}";

                        var extendCmd = new ExtendSessionCommand(cmdPayload.SessionId, additionalMinutes, idempotencyKey);
                        var extendRes = await _extendSessionHandler.HandleAsync(extendCmd, cancellationToken);

                        if (extendRes.IsSuccess)
                        {
                            _logger.LogInformation("Successfully reconciled offline EXTEND session command for SessionId {SessionId}.", cmdPayload.SessionId);
                            return BusinessReconciliationResult.Success("SUCCESS_SESSION_EXTENDED", extendRes.Value!.SessionId, extendRes.Value);
                        }

                        _logger.LogWarning("Offline EXTEND session reconciliation failed for SessionId {SessionId}: [{Code}] {Message}",
                            cmdPayload.SessionId, extendRes.ErrorCode, extendRes.ErrorMessage);

                        return BusinessReconciliationResult.Conflict("SESSION_EXTEND_FAILED", extendRes.ErrorMessage ?? "Failed to extend session.");
                    }

                default:
                    return BusinessReconciliationResult.Reject(OfflineReasonCode.MalformedPayload, $"Unsupported session action '{cmdPayload.Action}'.");
            }
        }

        private async Task<BusinessReconciliationResult> ReconcileInformationalEventAsync(
            OfflineQueueItem item,
            Workstation workstation,
            string eventTypeLabel,
            string payloadText,
            CancellationToken cancellationToken)
        {
            if (Guid.TryParse(item.EventId, out var eventGuid))
            {
                var auditEvent = new AuditEvent
                {
                    EventId = eventGuid,
                    EventType = eventTypeLabel,
                    WorkstationId = workstation.Id,
                    CorrelationId = item.EventId,
                    Priority = item.ReliabilityClass == EventReliabilityClass.Critical ? 1 : 2,
                    Timestamp = item.Timestamp != default ? item.Timestamp : DateTime.UtcNow,
                    Payload = payloadText
                };

                await _auditEventRepository.AddAsync(auditEvent, cancellationToken);
            }

            _logger.LogInformation("Reconciled informational event {EventId} ({Type}) for workstation {PcId}.",
                item.EventId, eventTypeLabel, workstation.PcId);

            return BusinessReconciliationResult.Success("SUCCESS_INFORMATIONAL", null, new { eventId = item.EventId, type = eventTypeLabel });
        }

        private async Task<BusinessReconciliationResult> ReconcileCommandAckAsync(
            OfflineQueueItem item,
            Workstation workstation,
            string payloadText,
            CancellationToken cancellationToken)
        {
            if (_remoteCommandManager != null)
            {
                try
                {
                    using var doc = JsonDocument.Parse(payloadText);
                    var root = doc.RootElement;
                    string cmdId = root.TryGetProperty("commandId", out var cProp) ? cProp.GetString() ?? "" : "";
                    string status = root.TryGetProperty("status", out var sProp) ? sProp.GetString() ?? "ACKNOWLEDGED" : "ACKNOWLEDGED";
                    string? reason = root.TryGetProperty("failureReason", out var rProp) ? rProp.GetString() : null;

                    if (!string.IsNullOrEmpty(cmdId))
                    {
                        await _remoteCommandManager.ProcessCommandAckAsync(cmdId, workstation.PcId, status, reason, cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error processing COMMAND_ACK payload for event {EventId}.", item.EventId);
                }
            }

            return BusinessReconciliationResult.Success("SUCCESS_COMMAND_ACK", null, new { eventId = item.EventId });
        }

        private async Task<BusinessReconciliationResult> ReconcileExecutionResultAsync(
            OfflineQueueItem item,
            Workstation workstation,
            string payloadText,
            CancellationToken cancellationToken)
        {
            if (_remoteCommandManager != null)
            {
                try
                {
                    using var doc = JsonDocument.Parse(payloadText);
                    var root = doc.RootElement;
                    string cmdId = root.TryGetProperty("commandId", out var cProp) ? cProp.GetString() ?? "" : "";
                    string status = root.TryGetProperty("status", out var sProp) ? sProp.GetString() ?? "Executed" : "Executed";
                    string? message = root.TryGetProperty("message", out var mProp) ? mProp.GetString() : null;
                    string? errorCode = root.TryGetProperty("errorCode", out var eProp) ? eProp.GetString() : null;
                    string? resultPayload = root.TryGetProperty("resultPayload", out var rProp) ? rProp.GetRawText() : null;

                    if (!string.IsNullOrEmpty(cmdId))
                    {
                        await _remoteCommandManager.ProcessCommandResultAsync(cmdId, workstation.PcId, status, message, errorCode, resultPayload, cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error processing EXECUTION_RESULT payload for event {EventId}.", item.EventId);
                }
            }

            return BusinessReconciliationResult.Success("SUCCESS_EXECUTION_RESULT", null, new { eventId = item.EventId });
        }

        private BusinessReconciliationResult ReconcileUnsupportedEvent(OfflineQueueItem item, string eventType)
        {
            _logger.LogWarning("Rejecting unsupported offline event {EventId} of type '{EventType}'.", item.EventId, eventType);
            return BusinessReconciliationResult.Reject("UNSUPPORTED_EVENT_TYPE", $"Offline event type '{eventType}' is not supported for business reconciliation.");
        }
    }
}
