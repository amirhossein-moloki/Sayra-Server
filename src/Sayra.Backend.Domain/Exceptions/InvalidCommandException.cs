namespace Sayra.Backend.Domain.Exceptions
{
    public class InvalidCommandException : DomainException
    {
        public InvalidCommandException(string message = "The provided command is invalid.")
            : base("INVALID_COMMAND", message)
        {
        }
    }
}
