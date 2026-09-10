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
