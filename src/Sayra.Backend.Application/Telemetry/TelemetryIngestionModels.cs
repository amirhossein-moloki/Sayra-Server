using System;
using Sayra.Backend.Domain.Enums;
using Sayra.Backend.Domain.Telemetry;
using Sayra.Backend.Domain.ValueObjects;

namespace Sayra.Backend.Application.Telemetry
{
    public sealed class TelemetryConnectionContext
    {
        public string ConnectionId { get; }
        public string PcId { get; }
        public Guid? WorkstationId { get; }
        public Guid? SiteId { get; }
        public Guid? OrganizationId { get; }
        public string? RemoteIpAddress { get; }

        public TelemetryConnectionContext(
            string connectionId,
            string pcId,
            Guid? workstationId = null,
            Guid? siteId = null,
            Guid? organizationId = null,
            string? remoteIpAddress = null)
        {
            ConnectionId = connectionId ?? string.Empty;
            PcId = pcId?.Trim().ToUpperInvariant() ?? string.Empty;
            WorkstationId = workstationId;
            SiteId = siteId;
            OrganizationId = organizationId;
            RemoteIpAddress = remoteIpAddress;
        }

        public WorkstationIdentity ToIdentity()
        {
            return new WorkstationIdentity(PcId, WorkstationId, SiteId, OrganizationId);
        }
    }

    public sealed class TelemetryIngestionResult
    {
        public TelemetryIngestionStatus Status { get; }
        public TelemetryRejectionReason RejectionReason { get; }
        public TelemetryMessageType MessageType { get; }
        public string? ErrorMessage { get; }
        public WorkstationIdentity? Identity { get; }
        public DateTime ServerReceivedAt { get; }
        public DateTime ProcessedAt { get; }
        public TelemetrySnapshot? Snapshot { get; }
        public OperationalEventSignal? EventSignal { get; }
        public HeartbeatSignal? HeartbeatSignal { get; }

        public bool IsAccepted => Status == TelemetryIngestionStatus.Accepted || Status == TelemetryIngestionStatus.Duplicate;

        private TelemetryIngestionResult(
            TelemetryIngestionStatus status,
            TelemetryRejectionReason rejectionReason,
            TelemetryMessageType messageType,
            string? errorMessage,
            WorkstationIdentity? identity,
            DateTime serverReceivedAt,
            DateTime processedAt,
            TelemetrySnapshot? snapshot = null,
            OperationalEventSignal? eventSignal = null,
            HeartbeatSignal? heartbeatSignal = null)
        {
            Status = status;
            RejectionReason = rejectionReason;
            MessageType = messageType;
            ErrorMessage = errorMessage;
            Identity = identity;
            ServerReceivedAt = serverReceivedAt;
            ProcessedAt = processedAt;
            Snapshot = snapshot;
            EventSignal = eventSignal;
            HeartbeatSignal = heartbeatSignal;
        }

        public static TelemetryIngestionResult AcceptedTelemetry(
            TelemetrySnapshot snapshot,
            DateTime serverReceivedAt,
            DateTime processedAt)
        {
            return new TelemetryIngestionResult(
                TelemetryIngestionStatus.Accepted,
                TelemetryRejectionReason.None,
                TelemetryMessageType.Telemetry,
                null,
                snapshot.Identity,
                serverReceivedAt,
                processedAt,
                snapshot: snapshot);
        }

        public static TelemetryIngestionResult AcceptedEvent(
            OperationalEventSignal eventSignal,
            DateTime serverReceivedAt,
            DateTime processedAt)
        {
            return new TelemetryIngestionResult(
                TelemetryIngestionStatus.Accepted,
                TelemetryRejectionReason.None,
                TelemetryMessageType.OperationalEvent,
                null,
                eventSignal.Identity,
                serverReceivedAt,
                processedAt,
                eventSignal: eventSignal);
        }

        public static TelemetryIngestionResult AcceptedHeartbeat(
            HeartbeatSignal heartbeatSignal,
            DateTime serverReceivedAt,
            DateTime processedAt)
        {
            return new TelemetryIngestionResult(
                TelemetryIngestionStatus.Accepted,
                TelemetryRejectionReason.None,
                TelemetryMessageType.Heartbeat,
                null,
                heartbeatSignal.Identity,
                serverReceivedAt,
                processedAt,
                heartbeatSignal: heartbeatSignal);
        }

        public static TelemetryIngestionResult DuplicateEvent(
            OperationalEventSignal eventSignal,
            DateTime serverReceivedAt,
            DateTime processedAt)
        {
            return new TelemetryIngestionResult(
                TelemetryIngestionStatus.Duplicate,
                TelemetryRejectionReason.DuplicateEvent,
                TelemetryMessageType.OperationalEvent,
                "Event has already been processed.",
                eventSignal.Identity,
                serverReceivedAt,
                processedAt,
                eventSignal: eventSignal);
        }

        public static TelemetryIngestionResult StaleTelemetry(
            WorkstationIdentity identity,
            TelemetryRejectionReason reason,
            string message,
            DateTime serverReceivedAt,
            DateTime processedAt)
        {
            return new TelemetryIngestionResult(
                TelemetryIngestionStatus.Stale,
                reason,
                TelemetryMessageType.Telemetry,
                message,
                identity,
                serverReceivedAt,
                processedAt);
        }

        public static TelemetryIngestionResult OutOfOrderTelemetry(
            WorkstationIdentity identity,
            string message,
            DateTime serverReceivedAt,
            DateTime processedAt)
        {
            return new TelemetryIngestionResult(
                TelemetryIngestionStatus.OutOfOrder,
                TelemetryRejectionReason.StaleTelemetrySnapshot,
                TelemetryMessageType.Telemetry,
                message,
                identity,
                serverReceivedAt,
                processedAt);
        }

        public static TelemetryIngestionResult IdentityMismatch(
            WorkstationIdentity identity,
            string message,
            TelemetryMessageType messageType,
            DateTime serverReceivedAt,
            DateTime processedAt)
        {
            return new TelemetryIngestionResult(
                TelemetryIngestionStatus.IdentityMismatch,
                TelemetryRejectionReason.IdentityMismatch,
                messageType,
                message,
                identity,
                serverReceivedAt,
                processedAt);
        }

        public static TelemetryIngestionResult Rejected(
            TelemetryRejectionReason reason,
            TelemetryMessageType messageType,
            string errorMessage,
            WorkstationIdentity? identity,
            DateTime serverReceivedAt,
            DateTime processedAt)
        {
            return new TelemetryIngestionResult(
                TelemetryIngestionStatus.Rejected,
                reason,
                messageType,
                errorMessage,
                identity,
                serverReceivedAt,
                processedAt);
        }
    }
}
