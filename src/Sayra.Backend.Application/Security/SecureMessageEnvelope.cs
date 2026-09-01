using System;

namespace Sayra.Backend.Application.Security
{
    public class SecureMessageEnvelope
    {
        public string Payload { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;
        public string? MessageId { get; set; }
        public string? CorrelationId { get; set; }
        public string? SessionId { get; set; }
        public long? SequenceNumber { get; set; }
        public string? MessageType { get; set; }
        public string? ProtocolVersion { get; set; } = "1.0";
    }
}
