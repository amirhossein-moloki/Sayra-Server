namespace Sayra.Backend.Domain.Exceptions
{
    public class InvalidDomainException : DomainException
    {
        public InvalidDomainException(string errorCode, string message)
            : base(errorCode, message)
        {
        }
    }
}
