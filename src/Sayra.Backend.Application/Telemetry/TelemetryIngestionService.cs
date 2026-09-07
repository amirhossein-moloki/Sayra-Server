using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain.Enums;
using Sayra.Backend.Domain.Telemetry;
using Sayra.Backend.Domain.ValueObjects;

namespace Sayra.Backend.Application.Telemetry
{
    public class TelemetryIngestionService : ITelemetryIngestionService
    {
        private readonly ISecurityEventService _securityEventService;
        private readonly ITelemetryIdempotencyService _idempotencyService;
        private readonly IWorkstationStateStore? _stateStore;
        private readonly ILogger<TelemetryIngestionService> _logger;

        public TelemetryIngestionService(
            ISecurityEventService securityEventService,
            ITelemetryIdempotencyService idempotencyService,
            ILogger<TelemetryIngestionService> logger)
            : this(securityEventService, idempotencyService, null, logger)
        {
        }

        public TelemetryIngestionService(
            ISecurityEventService securityEventService,
            ITelemetryIdempotencyService idempotencyService,
            IWorkstationStateStore? stateStore,
            ILogger<TelemetryIngestionService> logger)
        {
            _securityEventService = securityEventService ?? throw new ArgumentNullException(nameof(securityEventService));
            _idempotencyService = idempotencyService ?? throw new ArgumentNullException(nameof(idempotencyService));
            _stateStore = stateStore;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<TelemetryIngestionResult> IngestTelemetrySnapshotAsync(
            TelemetryConnectionContext connectionContext,
            TelemetryModel model,
            CancellationToken cancellationToken = default)
        {
            var serverReceivedAt = DateTime.UtcNow;
            var processedAt = serverReceivedAt;

            if (connectionContext == null || string.IsNullOrWhiteSpace(connectionContext.PcId))
            {
                return TelemetryIngestionResult.Rejected(
                    TelemetryRejectionReason.UnauthorizedContext,
                    TelemetryMessageType.Telemetry,
                    "Telemetry ingestion requires an authenticated connection context with a valid PC-ID.",
                    null,
                    serverReceivedAt,
                    processedAt);
            }

            var identity = connectionContext.ToIdentity();

            if (model == null)
            {
                return TelemetryIngestionResult.Rejected(
                    TelemetryRejectionReason.PayloadNull,
                    TelemetryMessageType.Telemetry,
                    "Telemetry model payload cannot be null.",
                    identity,
                    serverReceivedAt,
                    processedAt);
            }

            // Numeric Range & Boundary Validations
            if (model.Cpu < 0.0 || model.Cpu > 100.0)
            {
                return TelemetryIngestionResult.Rejected(
                    TelemetryRejectionReason.InvalidCpuRange,
                    TelemetryMessageType.Telemetry,
                    "CPU usage must be between 0% and 100%.",
                    identity,
                    serverReceivedAt,
                    processedAt);
            }

            if (model.Ram < 0.0)
            {
                return TelemetryIngestionResult.Rejected(
                    TelemetryRejectionReason.InvalidRamRange,
                    TelemetryMessageType.Telemetry,
                    "RAM usage cannot be negative.",
                    identity,
                    serverReceivedAt,
                    processedAt);
            }

            if (model.Uptime < 0.0)
            {
                return TelemetryIngestionResult.Rejected(
                    TelemetryRejectionReason.InvalidUptimeRange,
                    TelemetryMessageType.Telemetry,
                    "Uptime cannot be negative.",
                    identity,
                    serverReceivedAt,
                    processedAt);
            }

            if (model.RunningGameCpu.HasValue && (model.RunningGameCpu.Value < 0.0 || model.RunningGameCpu.Value > 100.0))
            {
                return TelemetryIngestionResult.Rejected(
                    TelemetryRejectionReason.InvalidGameCpuRange,
                    TelemetryMessageType.Telemetry,
                    "Running game CPU usage must be between 0% and 100%.",
                    identity,
                    serverReceivedAt,
                    processedAt);
            }

            if (model.RunningGameRam.HasValue && model.RunningGameRam.Value < 0.0)
            {
                return TelemetryIngestionResult.Rejected(
                    TelemetryRejectionReason.InvalidGameRamRange,
                    TelemetryMessageType.Telemetry,
                    "Running game RAM usage cannot be negative.",
                    identity,
                    serverReceivedAt,
                    processedAt);
            }

            if (model.RunningGameDuration.HasValue && model.RunningGameDuration.Value < 0.0)
            {
                return TelemetryIngestionResult.Rejected(
                    TelemetryRejectionReason.InvalidGameDurationRange,
                    TelemetryMessageType.Telemetry,
                    "Running game duration cannot be negative.",
                    identity,
                    serverReceivedAt,
                    processedAt);
            }

            if (!string.IsNullOrEmpty(model.RunningGameName) && model.RunningGameName.Length > 256)
            {
                return TelemetryIngestionResult.Rejected(
                    TelemetryRejectionReason.GameNameExceedsMaxLength,
                    TelemetryMessageType.Telemetry,
                    "Running game name exceeds maximum allowed length of 256 characters.",
                    identity,
                    serverReceivedAt,
                    processedAt);
            }

            if (model.TotalLaunches < 0)
            {
                return TelemetryIngestionResult.Rejected(
                    TelemetryRejectionReason.InvalidLaunchesRange,
                    TelemetryMessageType.Telemetry,
                    "Total launches count cannot be negative.",
                    identity,
                    serverReceivedAt,
                    processedAt);
            }

            if (model.TotalCrashes < 0)
            {
                return TelemetryIngestionResult.Rejected(
                    TelemetryRejectionReason.InvalidCrashesRange,
                    TelemetryMessageType.Telemetry,
                    "Total crashes count cannot be negative.",
                    identity,
                    serverReceivedAt,
                    processedAt);
            }

            if (model.TotalRestarts < 0)
            {
                return TelemetryIngestionResult.Rejected(
                    TelemetryRejectionReason.InvalidRestartsRange,
                    TelemetryMessageType.Telemetry,
                    "Total restarts count cannot be negative.",
                    identity,
                    serverReceivedAt,
                    processedAt);
            }

            // Timestamp Clock Skew Bounds
            if (model.Timestamp > serverReceivedAt.AddMinutes(5))
            {
                return TelemetryIngestionResult.Rejected(
                    TelemetryRejectionReason.TimestampInFuture,
                    TelemetryMessageType.Telemetry,
                    "Telemetry client timestamp is in the future beyond allowed clock skew limits (5 minutes).",
                    identity,
                    serverReceivedAt,
                    processedAt);
            }

            if (model.Timestamp < serverReceivedAt.AddHours(-24))
            {
                return TelemetryIngestionResult.Rejected(
                    TelemetryRejectionReason.TimestampExcessivelyOld,
                    TelemetryMessageType.Telemetry,
                    "Telemetry client timestamp is excessively old (> 24 hours).",
                    identity,
                    serverReceivedAt,
                    processedAt);
            }

            // Ordering / Stale Telemetry Check
            bool isStale = await _idempotencyService.IsStaleTelemetryAsync(identity.PcId, model.Timestamp, cancellationToken);
            if (isStale)
            {
                _logger.LogInformation("Out-of-order or stale telemetry snapshot received for PC-ID {PcId}. Client timestamp: {ClientTs}", identity.PcId, model.Timestamp);
                return TelemetryIngestionResult.StaleTelemetry(
                    identity,
                    TelemetryRejectionReason.StaleTelemetrySnapshot,
                    "Telemetry snapshot timestamp is older than the latest recorded sample for this workstation.",
                    serverReceivedAt,
                    processedAt);
            }

            await _idempotencyService.RecordLatestTelemetryTimestampAsync(identity.PcId, model.Timestamp, cancellationToken);

            var snapshot = new TelemetrySnapshot(
                identity,
                model.Cpu,
                model.Ram,
                model.Uptime,
                model.RunningGameName,
                model.RunningGamePid,
                model.RunningGameCpu,
                model.RunningGameRam,
                model.RunningGameDuration,
                model.TotalLaunches,
                model.TotalCrashes,
                model.TotalRestarts,
                model.Timestamp,
                serverReceivedAt,
                processedAt);

            // Update Authoritative Real-Time Workstation State
            if (_stateStore != null)
            {
                await _stateStore.UpdateFromTelemetryAsync(identity, snapshot, connectionContext.ConnectionId, cancellationToken);
            }

            return TelemetryIngestionResult.AcceptedTelemetry(snapshot, serverReceivedAt, processedAt);
        }

        public async Task<TelemetryIngestionResult> IngestOperationalEventAsync(
            TelemetryConnectionContext connectionContext,
            ClientEventEnvelopeDto eventDto,
            CancellationToken cancellationToken = default)
        {
            var serverReceivedAt = DateTime.UtcNow;
            var processedAt = serverReceivedAt;

            if (connectionContext == null || string.IsNullOrWhiteSpace(connectionContext.PcId))
            {
                return TelemetryIngestionResult.Rejected(
                    TelemetryRejectionReason.UnauthorizedContext,
                    TelemetryMessageType.OperationalEvent,
                    "Operational event ingestion requires an authenticated connection context with a valid PC-ID.",
                    null,
                    serverReceivedAt,
                    processedAt);
            }

            var identity = connectionContext.ToIdentity();

            if (eventDto == null)
            {
                return TelemetryIngestionResult.Rejected(
                    TelemetryRejectionReason.PayloadNull,
                    TelemetryMessageType.OperationalEvent,
                    "Event envelope payload cannot be null.",
                    identity,
                    serverReceivedAt,
                    processedAt);
            }

            // Identity Binding Verification
            if (!identity.Matches(eventDto.ClientId) || !identity.Matches(eventDto.WorkstationId))
            {
                string mismatchDetails = $"Payload ClientId '{eventDto.ClientId}' or WorkstationId '{eventDto.WorkstationId}' does not match authenticated connection PC-ID '{identity.PcId}'.";
                _logger.LogWarning("Identity mismatch detected during event ingestion on connection {ConnectionId}: {Details}", connectionContext.ConnectionId, mismatchDetails);

                await _securityEventService.RecordSecurityEventAsync(
                    eventType: "TELEMETRY_IDENTITY_MISMATCH",
                    actorId: null,
                    actorType: "Workstation",
                    deviceId: identity.PcId,
                    organizationId: identity.OrganizationId,
                    siteId: identity.SiteId,
                    resourceType: "Workstation",
                    resourceId: identity.WorkstationId,
                    action: "IngestOperationalEvent",
                    result: "FAILURE",
                    failureReason: mismatchDetails,
                    correlationId: eventDto.CorrelationId,
                    traceId: null,
                    cancellationToken: cancellationToken);

                return TelemetryIngestionResult.IdentityMismatch(identity, mismatchDetails, TelemetryMessageType.OperationalEvent, serverReceivedAt, processedAt);
            }

            // Envelope Validation
            if (string.IsNullOrWhiteSpace(eventDto.EventId))
            {
                return TelemetryIngestionResult.Rejected(
                    TelemetryRejectionReason.MissingEventId,
                    TelemetryMessageType.OperationalEvent,
                    "EventId is required.",
                    identity,
                    serverReceivedAt,
                    processedAt);
            }

            if (string.IsNullOrWhiteSpace(eventDto.EventType))
            {
                return TelemetryIngestionResult.Rejected(
                    TelemetryRejectionReason.MissingEventType,
                    TelemetryMessageType.OperationalEvent,
                    "EventType is required.",
                    identity,
                    serverReceivedAt,
                    processedAt);
            }

            if (eventDto.Payload != null && eventDto.Payload.Length > 65536)
            {
                return TelemetryIngestionResult.Rejected(
                    TelemetryRejectionReason.OversizedPayload,
                    TelemetryMessageType.OperationalEvent,
                    "Event payload exceeds maximum allowed size of 64 KB.",
                    identity,
                    serverReceivedAt,
                    processedAt);
            }

            // Timestamp Validation
            var eventTimestamp = eventDto.OccurredAt == default ? serverReceivedAt : eventDto.OccurredAt;

            if (eventTimestamp > serverReceivedAt.AddMinutes(5))
            {
                return TelemetryIngestionResult.Rejected(
                    TelemetryRejectionReason.TimestampInFuture,
                    TelemetryMessageType.OperationalEvent,
                    "Event OccurredAt timestamp is in the future beyond allowed clock skew limits (5 minutes).",
                    identity,
                    serverReceivedAt,
                    processedAt);
            }

            if (eventTimestamp < serverReceivedAt.AddHours(-24))
            {
                return TelemetryIngestionResult.Rejected(
                    TelemetryRejectionReason.TimestampExcessivelyOld,
                    TelemetryMessageType.OperationalEvent,
                    "Event OccurredAt timestamp is excessively old (> 24 hours).",
                    identity,
                    serverReceivedAt,
                    processedAt);
            }

            var eventSignal = new OperationalEventSignal(
                eventDto.EventId,
                eventDto.EventType,
                identity,
                eventDto.SessionId,
                eventDto.CorrelationId,
                eventTimestamp,
                eventDto.Payload ?? "{}",
                serverReceivedAt,
                processedAt);

            // Deduplication Check
            bool isDuplicate = await _idempotencyService.IsDuplicateEventAsync(eventDto.EventId, cancellationToken);
            if (isDuplicate)
            {
                _logger.LogInformation("Duplicate operational event {EventId} received for PC-ID {PcId}. Safely ignored idempotently.", eventDto.EventId, identity.PcId);
                return TelemetryIngestionResult.DuplicateEvent(eventSignal, serverReceivedAt, processedAt);
            }

            await _idempotencyService.MarkEventProcessedAsync(eventDto.EventId, cancellationToken);

            // Update Authoritative Real-Time Workstation State
            if (_stateStore != null)
            {
                await _stateStore.UpdateFromOperationalEventAsync(identity, eventSignal, cancellationToken);
            }

            return TelemetryIngestionResult.AcceptedEvent(eventSignal, serverReceivedAt, processedAt);
        }

        public async Task<TelemetryIngestionResult> IngestHeartbeatAsync(
            TelemetryConnectionContext connectionContext,
            HeartbeatMessage heartbeat,
            CancellationToken cancellationToken = default)
        {
            var serverReceivedAt = DateTime.UtcNow;
            var processedAt = serverReceivedAt;

            if (connectionContext == null || string.IsNullOrWhiteSpace(connectionContext.PcId))
            {
                return TelemetryIngestionResult.Rejected(
                    TelemetryRejectionReason.UnauthorizedContext,
                    TelemetryMessageType.Heartbeat,
                    "Heartbeat ingestion requires an authenticated connection context with a valid PC-ID.",
                    null,
                    serverReceivedAt,
                    processedAt);
            }

            var identity = connectionContext.ToIdentity();

            if (heartbeat == null)
            {
                return TelemetryIngestionResult.Rejected(
                    TelemetryRejectionReason.PayloadNull,
                    TelemetryMessageType.Heartbeat,
                    "Heartbeat payload cannot be null.",
                    identity,
                    serverReceivedAt,
                    processedAt);
            }

            // Identity Binding Verification
            if (!identity.Matches(heartbeat.PcId))
            {
                string mismatchDetails = $"Heartbeat PcId '{heartbeat.PcId}' does not match authenticated connection PC-ID '{identity.PcId}'.";
                _logger.LogWarning("Identity mismatch detected during heartbeat ingestion on connection {ConnectionId}: {Details}", connectionContext.ConnectionId, mismatchDetails);

                await _securityEventService.RecordSecurityEventAsync(
                    eventType: "TELEMETRY_IDENTITY_MISMATCH",
                    actorId: null,
                    actorType: "Workstation",
                    deviceId: identity.PcId,
                    organizationId: identity.OrganizationId,
                    siteId: identity.SiteId,
                    resourceType: "Workstation",
                    resourceId: identity.WorkstationId,
                    action: "IngestHeartbeat",
                    result: "FAILURE",
                    failureReason: mismatchDetails,
                    correlationId: null,
                    traceId: null,
                    cancellationToken: cancellationToken);

                return TelemetryIngestionResult.IdentityMismatch(identity, mismatchDetails, TelemetryMessageType.Heartbeat, serverReceivedAt, processedAt);
            }

            var clientTs = heartbeat.Timestamp == default ? serverReceivedAt : heartbeat.Timestamp;

            if (clientTs > serverReceivedAt.AddMinutes(5))
            {
                return TelemetryIngestionResult.Rejected(
                    TelemetryRejectionReason.TimestampInFuture,
                    TelemetryMessageType.Heartbeat,
                    "Heartbeat timestamp is in the future beyond allowed clock skew limits (5 minutes).",
                    identity,
                    serverReceivedAt,
                    processedAt);
            }

            var heartbeatSignal = new HeartbeatSignal(identity, clientTs, serverReceivedAt, processedAt);

            // Update Authoritative Real-Time Workstation State
            if (_stateStore != null)
            {
                await _stateStore.UpdateFromHeartbeatAsync(identity, heartbeatSignal, connectionContext.ConnectionId, cancellationToken);
            }

            return TelemetryIngestionResult.AcceptedHeartbeat(heartbeatSignal, serverReceivedAt, processedAt);
        }
    }
}
