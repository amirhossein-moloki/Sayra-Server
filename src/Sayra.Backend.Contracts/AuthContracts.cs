using System;

namespace Sayra.Backend.Contracts
{
    public class AuthChallengeMessage
    {
        public string Type { get; set; } = "AUTH_CHALLENGE";
        public string Challenge { get; set; } = string.Empty;
    }

    public class AuthResponseMessage
    {
        public string Type { get; set; } = "AUTH_RESPONSE";

        private string _response = string.Empty;

        public string Response
        {
            get => _response;
            set => _response = value;
        }

        public string Hmac
        {
            get => _response;
            set => _response = value;
        }

        public string EncryptedSessionKey { get; set; } = string.Empty;
        public string Iv { get; set; } = string.Empty;
        public string PcId { get; set; } = string.Empty;
        public string Hostname { get; set; } = string.Empty;
        public string SiteId { get; set; } = string.Empty;
        public string ClientVersion { get; set; } = string.Empty;
    }

    public enum AuthenticationStatus
    {
        SUCCESS,
        FAILED
    }

    public class AuthStatusMessage
    {
        public string Type { get; set; } = "AUTH_STATUS";
        public string Status { get; set; } = string.Empty; // "SUCCESS" or "FAILED"
        public string? Message { get; set; }
        public string? ErrorCode { get; set; }
    }

    public class LogoutRequestDto
    {
        public string? SessionId { get; set; }
        public string? Reason { get; set; }
    }

    public class LogoutResponseDto
    {
        public bool IsSuccess { get; set; }
        public string? Message { get; set; }
        public string? ErrorCode { get; set; }
    }
}
