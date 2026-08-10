using System;

namespace Sayra.Backend.Contracts
{
    public class SecureMessageEnvelope
    {
        public string Payload { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;
    }
}
