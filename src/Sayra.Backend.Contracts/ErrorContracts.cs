using System;

namespace Sayra.Backend.Contracts
{
    public class ErrorResponseContract
    {
        public string Code { get; set; } = string.Empty; // "AUTH_FAILED", "DEVICE_NOT_REGISTERED", "INVALID_COMMAND", "SESSION_EXPIRED"
        public string Message { get; set; } = string.Empty;
        public string? CorrelationId { get; set; }
        public DateTime Timestamp { get; set; }
        public string? Details { get; set; }
    }
}
