namespace Sayra.Backend.Domain.Exceptions
{
    public class AuthFailedException : DomainException
    {
        public AuthFailedException(string message = "Authentication failed.")
            : base("AUTH_FAILED", message)
        {
        }
    }
}
