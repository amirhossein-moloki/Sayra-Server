using System;
using System.Collections.Generic;

namespace Sayra.Backend.Contracts
{
    public class OfflineQueueItem
    {
        public string EventId { get; set; } = string.Empty;
        public object Payload { get; set; } = null!;
        public DateTime Timestamp { get; set; }
    }

    public class OfflineBatchRequest
    {
        public string BatchId { get; set; } = string.Empty;
        public List<OfflineQueueItem> Items { get; set; } = new();
    }

    public class OfflineBatchAcknowledgment
    {
        public string BatchId { get; set; } = string.Empty;
        public int ProcessedCount { get; set; }
        public bool Success { get; set; }
    }
}
