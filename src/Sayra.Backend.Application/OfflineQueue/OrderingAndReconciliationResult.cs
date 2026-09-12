using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Application.OfflineQueue
{
    public class OrderingAndReconciliationResult
    {
        public string EventId { get; set; } = string.Empty;
        public string OrderingStatus { get; set; } = string.Empty;
        public string ReconciliationStatus { get; set; } = string.Empty;
        public string? ReasonCode { get; set; }
        public string? ErrorMessage { get; set; }
        public long ExpectedSequence { get; set; }
        public long ReceivedSequence { get; set; }
        public bool IsAcceptedForAck { get; set; }
        public ProcessedEvent? ProcessedEventEntity { get; set; }
    }
}
