using System;

namespace Sayra.Backend.Application.Security
{
    public class SecureMessageEnvelope
    {
        public string Payload { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;
    }
}
