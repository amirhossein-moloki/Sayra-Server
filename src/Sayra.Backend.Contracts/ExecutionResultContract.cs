using System;

namespace Sayra.Backend.Contracts
{
    public class ExecutionResultMessage
    {
        public string MessageType { get; set; } = "EXECUTION_RESULT";
        public string CommandId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty; // "Executed" / "SUCCEEDED" or "Failed" / "FAILED"
        public string Message { get; set; } = string.Empty;
        public string? ErrorCode { get; set; }
        public object? Result { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string? CorrelationId { get; set; }
    }
}
