using System;

namespace Sayra.Backend.Contracts
{
    public class CommunicationErrorMessage
    {
        public string MessageType { get; set; } = "ERROR";
        public string ErrorCode { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? CorrelationId { get; set; }
        public bool Retryable { get; set; } = false;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public static class CommunicationErrorCode
    {
        public const string MalformedMessage = "MALFORMED_MESSAGE";
        public const string ProtocolViolation = "PROTOCOL_VIOLATION";
        public const string AuthenticationFailed = "AUTHENTICATION_FAILED";
        public const string AuthorizationFailed = "AUTHORIZATION_FAILED";
        public const string SecurityViolation = "SECURITY_VIOLATION";
        public const string RateLimitExceeded = "RATE_LIMIT_EXCEEDED";
        public const string InvalidStateTransition = "INVALID_STATE_TRANSITION";
        public const string WorkstationIneligible = "WORKSTATION_INELIGIBLE";
        public const string CrossWorkstationForgery = "CROSS_WORKSTATION_FORGERY";
        public const string InternalServerError = "INTERNAL_SERVER_ERROR";
    }
}
