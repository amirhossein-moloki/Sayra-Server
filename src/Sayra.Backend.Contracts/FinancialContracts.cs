using System;

namespace Sayra.Backend.Contracts
{
    public class AccountBalanceResponseDto
    {
        public Guid GamerAccountId { get; set; }
        public Guid GamerEntityId { get; set; }
        public string AccountNumber { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Currency { get; set; } = string.Empty;
        public decimal Balance { get; set; }
        public decimal BonusBalance { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class LedgerEntryResponseDto
    {
        public Guid Id { get; set; }
        public Guid GamerAccountId { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = string.Empty;
        public string Direction { get; set; } = string.Empty;
        public string EntryType { get; set; } = string.Empty;
        public string Reference { get; set; } = string.Empty;
        public string CorrelationId { get; set; } = string.Empty;
        public string? Actor { get; set; }
        public string? Description { get; set; }
        public decimal BalanceAfter { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    public class CreditAccountRequestDto
    {
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "SAY";
        public string Reference { get; set; } = string.Empty;
        public string EntryType { get; set; } = "DEPOSIT";
        public string? Description { get; set; }
        public string? Actor { get; set; }
    }

    public class DebitAccountRequestDto
    {
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "SAY";
        public string Reference { get; set; } = string.Empty;
        public string EntryType { get; set; } = "WITHDRAWAL";
        public string? Description { get; set; }
        public string? Actor { get; set; }
    }
}
