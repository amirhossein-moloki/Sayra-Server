using System;
using Sayra.Backend.Domain.Exceptions;

namespace Sayra.Backend.Domain.Entities
{
    public class SessionExtension : BaseEntity
    {
        public Guid SessionExtensionId
        {
            get => Id;
            set => Id = value;
        }

        public Guid SessionId { get; set; }
        public TimeSpan ExtendedDuration { get; set; }
        public decimal Cost { get; set; }
        public string Currency { get; set; } = "SAY";
        public string IdempotencyKey { get; set; } = string.Empty;
        public Guid? FinancialTransactionId { get; set; }

        public void NormalizeAndValidate()
        {
            if (SessionId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_SESSION_ID", "SessionId is required for SessionExtension.");
            }

            if (ExtendedDuration <= TimeSpan.Zero)
            {
                throw new InvalidDomainException("INVALID_EXTENSION_DURATION", "ExtendedDuration must be greater than zero.");
            }

            if (Cost < 0)
            {
                throw new InvalidDomainException("INVALID_EXTENSION_COST", "Cost cannot be negative.");
            }

            Currency = string.IsNullOrWhiteSpace(Currency) ? "SAY" : Currency.Trim().ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(IdempotencyKey))
            {
                throw new InvalidDomainException("INVALID_IDEMPOTENCY_KEY", "IdempotencyKey is required for SessionExtension.");
            }

            IdempotencyKey = IdempotencyKey.Trim();
        }
    }
}
