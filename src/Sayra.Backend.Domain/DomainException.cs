using System;

namespace Sayra.Backend.Domain
{
    public abstract class DomainException : Exception
    {
        public string ErrorCode { get; }

        protected DomainException(string errorCode, string message) : base(message)
        {
            ErrorCode = errorCode;
        }
    }
}
