namespace Sayra.Backend.Domain.Exceptions
{
    public class SessionExpiredException : DomainException
    {
        public SessionExpiredException(string message = "The session has expired.")
            : base("SESSION_EXPIRED", message)
        {
        }
    }
}
