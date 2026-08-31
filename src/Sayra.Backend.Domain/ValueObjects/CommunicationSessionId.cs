using System;

namespace Sayra.Backend.Domain.ValueObjects
{
    public readonly record struct CommunicationSessionId
    {
        public Guid Value { get; }

        public CommunicationSessionId(Guid value)
        {
            if (value == Guid.Empty)
            {
                throw new ArgumentException("CommunicationSessionId cannot be empty.", nameof(value));
            }
            Value = value;
        }

        public static CommunicationSessionId New() => new(Guid.NewGuid());
        public static CommunicationSessionId Parse(string input) => new(Guid.Parse(input));

        public static implicit operator Guid(CommunicationSessionId id) => id.Value;
        public static implicit operator CommunicationSessionId(Guid value) => new(value);

        public override string ToString() => Value.ToString();
    }
}
