using System;

namespace Sayra.Backend.Application.Abstractions.Security
{
    public class AuthChallengeDto
    {
        public string Type { get; set; } = "AUTH_CHALLENGE";
        public string Challenge { get; set; } = string.Empty;
    }

    public class AuthResponseDto
    {
        public string Hmac { get; set; } = string.Empty;
        public string EncryptedSessionKey { get; set; } = string.Empty;
        public string Iv { get; set; } = string.Empty;
        public string PcId { get; set; } = string.Empty;
        public string Hostname { get; set; } = string.Empty;
        public string SiteId { get; set; } = string.Empty;
        public string ClientVersion { get; set; } = string.Empty;
    }

    public class AuthStatusDto
    {
        public string Type { get; set; } = "AUTH_STATUS";
        public string Status { get; set; } = string.Empty; // "SUCCESS" or "FAILED"
        public string? ErrorCode { get; set; } // "AUTH_FAILED" or "DEVICE_NOT_REGISTERED"
        public string? Message { get; set; }
    }

    public class SecureMessageEnvelope
    {
        public string Payload { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;
    }
}
