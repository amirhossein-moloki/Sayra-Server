using System;

namespace Sayra.Backend.Contracts
{
    public class MessageMetadata
    {
        public string MessageId { get; set; } = Guid.NewGuid().ToString("N");
        public string MessageType { get; set; } = string.Empty;
        public string ProtocolVersion { get; set; } = "1.0";
        public string CorrelationId { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string? SenderId { get; set; }
        public string? TargetId { get; set; }
        public string? InReplyToMessageId { get; set; }

        public static MessageMetadata Create(
            string messageType,
            string? correlationId = null,
            string? senderId = null,
            string? targetId = null,
            string? inReplyToMessageId = null)
        {
            return new MessageMetadata
            {
                MessageId = Guid.NewGuid().ToString("N"),
                MessageType = messageType,
                ProtocolVersion = "1.0",
                CorrelationId = correlationId ?? string.Empty,
                Timestamp = DateTime.UtcNow,
                SenderId = senderId,
                TargetId = targetId,
                InReplyToMessageId = inReplyToMessageId
            };
        }
    }

    public class CommunicationMessage<TPayload>
    {
        public MessageMetadata Metadata { get; set; } = new();
        public TPayload? Payload { get; set; }

        public CommunicationMessage() { }

        public CommunicationMessage(MessageMetadata metadata, TPayload? payload)
        {
            Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            Payload = payload;
        }

        public static CommunicationMessage<TPayload> Create(
            string messageType,
            TPayload? payload,
            string? correlationId = null,
            string? senderId = null,
            string? targetId = null)
        {
            return new CommunicationMessage<TPayload>
            {
                Metadata = MessageMetadata.Create(messageType, correlationId, senderId, targetId),
                Payload = payload
            };
        }
    }

    public class CommunicationMessage : CommunicationMessage<object>
    {
        public CommunicationMessage() { }

        public CommunicationMessage(MessageMetadata metadata, object? payload)
            : base(metadata, payload)
        {
        }
    }
}
