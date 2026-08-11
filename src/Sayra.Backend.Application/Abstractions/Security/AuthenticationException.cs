using System;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Application.Abstractions.Security
{
    public class AuthenticationException : DomainException
    {
        public AuthenticationException(string message)
            : base("AUTH_FAILED", message)
        {
        }

        public AuthenticationException(string errorCode, string message)
            : base(errorCode, message)
        {
        }
    }
}
