using Sayra.Backend.Application.Abstractions.Transport;

namespace Sayra.Backend.Application.Abstractions.Security
{
    public class AuthenticationResult
    {
        public bool IsSuccess { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public byte[]? SessionKey { get; set; }
        public ConnectionLifecycleState NewState { get; set; }
    }
}
