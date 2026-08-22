namespace Sayra.Backend.Application.Abstractions.Security
{
    public class AuthorizationResult
    {
        public bool IsAllowed { get; private set; }
        public string? FailureReason { get; private set; }
        public string? ErrorCode { get; private set; }

        private AuthorizationResult(bool isAllowed, string? failureReason, string? errorCode)
        {
            IsAllowed = isAllowed;
            FailureReason = failureReason;
            ErrorCode = errorCode;
        }

        public static AuthorizationResult Allowed() => new AuthorizationResult(true, null, null);

        public static AuthorizationResult Denied(string reason, string errorCode = "FORBIDDEN")
            => new AuthorizationResult(false, reason, errorCode);
    }
}
