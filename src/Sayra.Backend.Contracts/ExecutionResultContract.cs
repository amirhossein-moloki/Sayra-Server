using System;

namespace Sayra.Backend.Contracts
{
    public class ExecutionResultMessage
    {
        public string CommandId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty; // "Executed" or "Failed"
        public string Message { get; set; } = string.Empty;
        public object? Result { get; set; }
        public DateTime Timestamp { get; set; }
        public string? CorrelationId { get; set; }
    }
}
