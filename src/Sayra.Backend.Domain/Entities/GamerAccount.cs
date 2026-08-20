using System;
using Sayra.Backend.Domain.Exceptions;
using Sayra.Backend.Shared;

namespace Sayra.Backend.Domain
{
    public class GamerAccount : BaseEntity
    {
        public Guid GamerEntityId { get; set; }
        public string AccountNumber { get; set; } = string.Empty;
        public string Status { get; set; } = "Active"; // Active, Frozen, Closed
        public string Currency { get; set; } = "SAY";
        public decimal Balance { get; set; } = 0.00m;
        public decimal BonusBalance { get; set; } = 0.00m;

        // Optimistic concurrency token
        public uint RowVersion { get; set; }

        public void NormalizeAndValidate()
        {
            if (GamerEntityId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_GAMER_ID", "GamerEntityId is required for GamerAccount.");
            }

            if (string.IsNullOrWhiteSpace(AccountNumber))
            {
                AccountNumber = $"ACC-{GamerEntityId.ToString("N").Substring(0, 8).ToUpperInvariant()}";
            }
            AccountNumber = AccountNumber.Trim().ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(Currency))
            {
                Currency = "SAY";
            }
            Currency = Currency.Trim().ToUpperInvariant();

            var statusTrimmed = (Status ?? string.Empty).Trim();
            if (statusTrimmed.Equals("Active", StringComparison.OrdinalIgnoreCase)) Status = "Active";
            else if (statusTrimmed.Equals("Frozen", StringComparison.OrdinalIgnoreCase)) Status = "Frozen";
            else if (statusTrimmed.Equals("Closed", StringComparison.OrdinalIgnoreCase)) Status = "Closed";
            else
            {
                throw new InvalidDomainException("INVALID_STATUS", $"Invalid GamerAccount status: {Status}");
            }
        }

        public bool CanTransact()
        {
            return Status != null && Status.Equals("Active", StringComparison.OrdinalIgnoreCase);
        }

        public void Freeze()
        {
            Status = "Frozen";
            UpdatedAt = DateTime.UtcNow;
        }

        public void Unfreeze()
        {
            Status = "Active";
            UpdatedAt = DateTime.UtcNow;
        }

        public void Close()
        {
            Status = "Closed";
            UpdatedAt = DateTime.UtcNow;
        }

        public LedgerEntry Credit(
            Money money,
            string reference,
            string entryType = "CREDIT",
            string? correlationId = null,
            string? actor = null,
            string? description = null)
        {
            if (!CanTransact())
            {
                throw new InvalidDomainException("ACCOUNT_DISABLED", $"Account '{Id}' is in status '{Status}' and cannot process financial operations.");
            }

            if (money == null || money.Amount <= 0)
            {
                throw new InvalidDomainException("INVALID_AMOUNT", "Credit amount must be strictly greater than zero.");
            }

            if (!string.Equals(money.Currency, Currency, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDomainException("CURRENCY_MISMATCH", $"Cannot credit amount in currency '{money.Currency}' to account in '{Currency}'.");
            }

            Balance += money.Amount;
            UpdatedAt = DateTime.UtcNow;

            var entry = new LedgerEntry
            {
                GamerAccountId = Id,
                Amount = money.Amount,
                Currency = Currency,
                Direction = "CREDIT",
                EntryType = entryType,
                Reference = reference,
                CorrelationId = correlationId ?? string.Empty,
                Actor = actor,
                Description = description,
                BalanceAfter = Balance,
                CreatedAtUtc = DateTime.UtcNow
            };

            entry.NormalizeAndValidate();
            return entry;
        }

        public LedgerEntry Debit(
            Money money,
            string reference,
            string entryType = "DEBIT",
            string? correlationId = null,
            string? actor = null,
            string? description = null)
        {
            if (!CanTransact())
            {
                throw new InvalidDomainException("ACCOUNT_DISABLED", $"Account '{Id}' is in status '{Status}' and cannot process financial operations.");
            }

            if (money == null || money.Amount <= 0)
            {
                throw new InvalidDomainException("INVALID_AMOUNT", "Debit amount must be strictly greater than zero.");
            }

            if (!string.Equals(money.Currency, Currency, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDomainException("CURRENCY_MISMATCH", $"Cannot debit amount in currency '{money.Currency}' from account in '{Currency}'.");
            }

            var typeUpper = (entryType ?? string.Empty).Trim().ToUpperInvariant();
            if (typeUpper != "USAGE_CHARGE" && Balance < money.Amount)
            {
                throw new InvalidDomainException("INSUFFICIENT_BALANCE", $"Insufficient funds. Current balance is {Balance} {Currency}, attempted debit of {money.Amount} {money.Currency}.");
            }

            Balance -= money.Amount;
            UpdatedAt = DateTime.UtcNow;

            var entry = new LedgerEntry
            {
                GamerAccountId = Id,
                Amount = money.Amount,
                Currency = Currency,
                Direction = "DEBIT",
                EntryType = entryType,
                Reference = reference,
                CorrelationId = correlationId ?? string.Empty,
                Actor = actor,
                Description = description,
                BalanceAfter = Balance,
                CreatedAtUtc = DateTime.UtcNow
            };

            entry.NormalizeAndValidate();
            return entry;
        }
    }
}
