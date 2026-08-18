// PHASE 03 — STAGE 03-09: Payment / Financial Transaction Engine & Idempotency
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

    public class CreatePaymentRequestDto
    {
        public Guid GamerAccountId { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "SAY";
        public string PaymentMethod { get; set; } = "ACCOUNT_BALANCE";
        public string IdempotencyKey { get; set; } = string.Empty;
        public string Reference { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? CorrelationId { get; set; }
    }

    public class PaymentResponseDto
    {
        public Guid Id { get; set; }
        public Guid GamerAccountId { get; set; }
        public Guid? FinancialTransactionId { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "SAY";
        public string Status { get; set; } = string.Empty;
        public string PaymentMethod { get; set; } = string.Empty;
        public string IdempotencyKey { get; set; } = string.Empty;
        public string Reference { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? FailureReason { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
    }

    public class ProcessTransactionRequestDto
    {
        public Guid GamerAccountId { get; set; }
        public string OperationType { get; set; } = "DEPOSIT";
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "SAY";
        public string IdempotencyKey { get; set; } = string.Empty;
        public string ReferenceId { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? CorrelationId { get; set; }
    }

    public class FinancialTransactionResponseDto
    {
        public Guid Id { get; set; }
        public Guid GamerAccountId { get; set; }
        public string OperationType { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "SAY";
        public string Status { get; set; } = string.Empty;
        public string IdempotencyKey { get; set; } = string.Empty;
        public string RequestFingerprint { get; set; } = string.Empty;
        public string CorrelationId { get; set; } = string.Empty;
        public string ReferenceId { get; set; } = string.Empty;
        public Guid? OriginalTransactionId { get; set; }
        public Guid? LedgerEntryId { get; set; }
        public string? FailureReason { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
        public DateTime? ReversedAtUtc { get; set; }
    }

    public class ReverseTransactionRequestDto
    {
        public Guid OriginalTransactionId { get; set; }
        public string IdempotencyKey { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string? CorrelationId { get; set; }
    }
}
