using System;
using System.Collections.Generic;

namespace Sayra.Backend.Contracts
{
    public static class EventReliabilityClass
    {
        public const string Critical = "CRITICAL";
        public const string Important = "IMPORTANT";
        public const string Normal = "NORMAL";
        public const string Ephemeral = "EPHEMERAL";
        public const string NotQueueable = "NOT_QUEUEABLE";
    }

    public static class OfflineSyncMessageTypes
    {
        public const string OfflineSyncBatch = "OFFLINE_SYNC_BATCH";
        public const string OfflineSyncAck = "OFFLINE_SYNC_ACK";
    }

    public static class OfflineOrderingStatus
    {
        public const string InOrder = "IN_ORDER";
        public const string OutOfOrder = "OUT_OF_ORDER";
        public const string GapDetected = "GAP_DETECTED";
        public const string Duplicate = "DUPLICATE";
        public const string Stale = "STALE";
        public const string SequenceConflict = "SEQUENCE_CONFLICT";
        public const string Unordered = "UNORDERED";
    }

    public static class OfflineReconciliationStatus
    {
        public const string Accepted = "ACCEPTED";
        public const string ReadyForReconciliation = "READY_FOR_RECONCILIATION";
        public const string WaitingForSequence = "WAITING_FOR_SEQUENCE";
        public const string Duplicate = "DUPLICATE";
        public const string SequenceConflict = "SEQUENCE_CONFLICT";
        public const string Conflict = "CONFLICT";
        public const string Expired = "EXPIRED";
        public const string Invalid = "INVALID";
        public const string Rejected = "REJECTED";
    }

    public static class OfflineReasonCode
    {
        public const string IdentityMismatch = "IDENTITY_MISMATCH";
        public const string WorkstationNotFound = "WORKSTATION_NOT_FOUND";
        public const string SiteMismatch = "SITE_MISMATCH";
        public const string ExpiredRetention = "EXPIRED_RETENTION";
        public const string FutureTimestampClockSkew = "FUTURE_TIMESTAMP_CLOCK_SKEW";
        public const string SequenceGapWait = "SEQUENCE_GAP_WAIT";
        public const string StaleSequence = "STALE_SEQUENCE";
        public const string PayloadHashConflict = "PAYLOAD_HASH_CONFLICT";
        public const string InvalidSessionReference = "INVALID_SESSION_REFERENCE";
        public const string MalformedPayload = "MALFORMED_PAYLOAD";
        public const string SuccessInOrder = "SUCCESS_IN_ORDER";
        public const string SuccessUnordered = "SUCCESS_UNORDERED";
        public const string SuccessGapAccepted = "SUCCESS_GAP_ACCEPTED";
    }

    public class OfflineQueueItem
    {
        public string EventId { get; set; } = string.Empty;
        public string EventType { get; set; } = string.Empty;
        public long SequenceNumber { get; set; }
        public string ReliabilityClass { get; set; } = EventReliabilityClass.Normal;
        public string ContractVersion { get; set; } = "1.0";
        public object Payload { get; set; } = null!;
        public DateTime Timestamp { get; set; }
    }

    public class OfflineBatchRequest
    {
        public string BatchId { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string WorkstationId { get; set; } = string.Empty;
        public string ContractVersion { get; set; } = "1.0";
        public List<OfflineQueueItem> Items { get; set; } = new();
    }

    public class OfflineBatchAcknowledgment
    {
        public string BatchId { get; set; } = string.Empty;
        public int ProcessedCount { get; set; }
        public bool Success { get; set; }
        public List<string> AcknowledgedEventIds { get; set; } = new();
        public List<string> RejectedEventIds { get; set; } = new();
        public string? ErrorMessage { get; set; }
    }
}
