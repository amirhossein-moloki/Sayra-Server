using System;

namespace Sayra.Backend.Domain.ValueObjects
{
    public readonly record struct MessageId
    {
        public string Value { get; }

        public MessageId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("MessageId cannot be null or empty.", nameof(value));
            }
            Value = value;
        }

        public static MessageId New() => new(Guid.NewGuid().ToString("N"));

        public static implicit operator string(MessageId id) => id.Value;
        public static implicit operator MessageId(string value) => new(value);

        public override string ToString() => Value;
    }
}
