// PHASE 03 — STAGE 03-09: Payment / Financial Transaction Engine & Idempotency
using System;
using System.Security.Cryptography;
using System.Text;
using Sayra.Backend.Domain.Exceptions;

namespace Sayra.Backend.Domain
{
    public class FinancialTransaction : BaseEntity
    {
        public Guid GamerAccountId { get; set; }
        public string OperationType { get; set; } = string.Empty; // DEPOSIT, WITHDRAWAL, SESSION_CHARGE, REFUND, ADJUSTMENT, PAYMENT, RESERVATION_HOLD, RESERVATION_RELEASE
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "SAY";
        public string Status { get; set; } = "PENDING"; // PENDING, COMPLETED, FAILED, REVERSED, CANCELLED
        public string IdempotencyKey { get; set; } = string.Empty;
        public string RequestFingerprint { get; set; } = string.Empty;
        public string CorrelationId { get; set; } = string.Empty;
        public string ReferenceId { get; set; } = string.Empty;
        public Guid? OriginalTransactionId { get; set; }
        public Guid? LedgerEntryId { get; set; }
        public string? FailureReason { get; set; }
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAtUtc { get; set; }
        public DateTime? ReversedAtUtc { get; set; }

        public static string ComputeFingerprint(Guid accountId, string operationType, decimal amount, string currency, string referenceId)
        {
            var raw = $"{accountId:N}|{(operationType ?? string.Empty).Trim().ToUpperInvariant()}|{amount:F4}|{(currency ?? string.Empty).Trim().ToUpperInvariant()}|{(referenceId ?? string.Empty).Trim()}";
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(raw));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        public void NormalizeAndValidate()
        {
            if (GamerAccountId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_ACCOUNT_ID", "GamerAccountId is required for FinancialTransaction.");
            }

            if (Amount <= 0)
            {
                throw new InvalidDomainException("INVALID_AMOUNT", "Transaction amount must be strictly greater than zero.");
            }

            if (string.IsNullOrWhiteSpace(Currency))
            {
                Currency = "SAY";
            }
            Currency = Currency.Trim().ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(OperationType))
            {
                throw new InvalidDomainException("INVALID_OPERATION_TYPE", "OperationType is required.");
            }
            OperationType = OperationType.Trim().ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(IdempotencyKey))
            {
                throw new InvalidDomainException("INVALID_IDEMPOTENCY_KEY", "IdempotencyKey is required for financial operations.");
            }
            IdempotencyKey = IdempotencyKey.Trim();
            if (IdempotencyKey.Length > 100)
            {
                throw new InvalidDomainException("IDEMPOTENCY_KEY_TOO_LONG", "IdempotencyKey cannot exceed 100 characters.");
            }

            if (string.IsNullOrWhiteSpace(RequestFingerprint))
            {
                RequestFingerprint = ComputeFingerprint(GamerAccountId, OperationType, Amount, Currency, ReferenceId);
            }

            Status = (Status ?? string.Empty).Trim().ToUpperInvariant();
            if (Status != "PENDING" && Status != "COMPLETED" && Status != "FAILED" && Status != "REVERSED" && Status != "CANCELLED")
            {
                throw new InvalidDomainException("INVALID_STATUS", $"Invalid transaction status: {Status}");
            }

            CorrelationId = (CorrelationId ?? string.Empty).Trim();
            ReferenceId = (ReferenceId ?? string.Empty).Trim();

            if (CreatedAtUtc == default)
            {
                CreatedAtUtc = DateTime.UtcNow;
            }
        }

        public void Complete(Guid ledgerEntryId)
        {
            if (Status != "PENDING")
            {
                throw new InvalidDomainException("INVALID_STATE_TRANSITION", $"Cannot complete transaction in status '{Status}'. Only PENDING transactions can be completed.");
            }

            if (ledgerEntryId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_LEDGER_ENTRY", "A valid LedgerEntryId is required to complete a FinancialTransaction.");
            }

            Status = "COMPLETED";
            LedgerEntryId = ledgerEntryId;
            CompletedAtUtc = DateTime.UtcNow;
            UpdatedAt = DateTime.UtcNow;
        }

        public void Fail(string reason)
        {
            if (Status != "PENDING")
            {
                throw new InvalidDomainException("INVALID_STATE_TRANSITION", $"Cannot mark transaction as FAILED from status '{Status}'.");
            }

            Status = "FAILED";
            FailureReason = reason?.Trim();
            UpdatedAt = DateTime.UtcNow;
        }

        public void Cancel(string reason)
        {
            if (Status != "PENDING")
            {
                throw new InvalidDomainException("INVALID_STATE_TRANSITION", $"Cannot cancel transaction in status '{Status}'.");
            }

            Status = "CANCELLED";
            FailureReason = reason?.Trim();
            UpdatedAt = DateTime.UtcNow;
        }

        public void Reverse(Guid reversalTransactionId)
        {
            if (Status != "COMPLETED")
            {
                throw new InvalidDomainException("INVALID_STATE_TRANSITION", $"Cannot reverse transaction in status '{Status}'. Only COMPLETED transactions can be reversed.");
            }

            if (reversalTransactionId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_REVERSAL_TRANSACTION", "A valid reversal transaction ID is required.");
            }

            Status = "REVERSED";
            ReversedAtUtc = DateTime.UtcNow;
            UpdatedAt = DateTime.UtcNow;
        }
    }
}
