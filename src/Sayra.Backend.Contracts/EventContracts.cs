using System;

namespace Sayra.Backend.Contracts
{
    public class EventMessage
    {
        public string EventType { get; set; } = string.Empty;
        public string EventId { get; set; } = string.Empty;
        public string PcId { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public object? SessionInformation { get; set; }
        public object? Details { get; set; }
        public string? CorrelationId { get; set; }
    }
}
