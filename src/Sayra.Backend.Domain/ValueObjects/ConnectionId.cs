using System;

namespace Sayra.Backend.Domain.ValueObjects
{
    public readonly record struct ConnectionId
    {
        public string Value { get; }

        public ConnectionId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("ConnectionId cannot be null or empty.", nameof(value));
            }
            Value = value;
        }

        public static ConnectionId New() => new(Guid.NewGuid().ToString("N"));

        public static implicit operator string(ConnectionId id) => id.Value;
        public static implicit operator ConnectionId(string value) => new(value);

        public override string ToString() => Value;
    }
}
