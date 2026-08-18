using System;
using Sayra.Backend.Domain.Exceptions;

namespace Sayra.Backend.Domain
{
    public class LedgerEntry : BaseEntity
    {
        public Guid GamerAccountId { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "SAY";
        public string Direction { get; set; } = string.Empty; // CREDIT or DEBIT
        public string EntryType { get; set; } = string.Empty;  // DEPOSIT, WITHDRAWAL, ADJUSTMENT, SESSION_PAYMENT, etc.
        public string Reference { get; set; } = string.Empty;
        public string CorrelationId { get; set; } = string.Empty;
        public string? Actor { get; set; }
        public string? Description { get; set; }
        public decimal BalanceAfter { get; set; }
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public void NormalizeAndValidate()
        {
            if (GamerAccountId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_ACCOUNT_ID", "GamerAccountId is required for LedgerEntry.");
            }

            if (Amount <= 0)
            {
                throw new InvalidDomainException("INVALID_AMOUNT", "Ledger entry amount must be strictly greater than zero.");
            }

            if (string.IsNullOrWhiteSpace(Currency))
            {
                Currency = "SAY";
            }
            Currency = Currency.Trim().ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(Direction))
            {
                throw new InvalidDomainException("INVALID_DIRECTION", "Direction is required for LedgerEntry.");
            }

            var dir = Direction.Trim().ToUpperInvariant();
            if (dir != "CREDIT" && dir != "DEBIT")
            {
                throw new InvalidDomainException("INVALID_DIRECTION", $"Invalid direction '{Direction}'. Must be CREDIT or DEBIT.");
            }
            Direction = dir;

            if (string.IsNullOrWhiteSpace(EntryType))
            {
                EntryType = "GENERAL";
            }
            EntryType = EntryType.Trim().ToUpperInvariant();

            Reference = (Reference ?? string.Empty).Trim();
            CorrelationId = (CorrelationId ?? string.Empty).Trim();
            Actor = Actor?.Trim();
            Description = Description?.Trim();

            if (CreatedAtUtc == default)
            {
                CreatedAtUtc = DateTime.UtcNow;
            }
        }
    }
}
