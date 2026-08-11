namespace Sayra.Backend.Domain.Exceptions
{
    public class ProtocolException : DomainException
    {
        public const string FrameTooLarge = "FRAME_TOO_LARGE";
        public const string InvalidJson = "INVALID_JSON";
        public const string UnknownMessageType = "UNKNOWN_MESSAGE_TYPE";
        public const string InvalidMessage = "INVALID_MESSAGE";
        public const string ConnectionClosed = "CONNECTION_CLOSED";
        public const string ProtocolError = "PROTOCOL_ERROR";

        public ProtocolException(string errorCode, string message)
            : base(errorCode, message)
        {
        }
    }
}
