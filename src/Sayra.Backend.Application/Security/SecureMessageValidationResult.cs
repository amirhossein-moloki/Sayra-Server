using System;

namespace Sayra.Backend.Application.Security
{
    public class SecureMessageValidationResult
    {
        public bool IsSuccess { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public string? PlaintextPayload { get; set; }
    }
}
