// PHASE 03 — STAGE 03-09: Payment / Financial Transaction Engine & Idempotency
using System;
using Sayra.Backend.Domain.Exceptions;

namespace Sayra.Backend.Domain
{
    public class Payment : BaseEntity
    {
        public Guid GamerAccountId { get; set; }
        public Guid? FinancialTransactionId { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "SAY";
        public string Status { get; set; } = "PENDING"; // PENDING, COMPLETED, FAILED, CANCELLED
        public string PaymentMethod { get; set; } = "ACCOUNT_BALANCE"; // ACCOUNT_BALANCE, CASH, CARD, INTERNAL
        public string IdempotencyKey { get; set; } = string.Empty;
        public string Reference { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? FailureReason { get; set; }
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAtUtc { get; set; }

        public void NormalizeAndValidate()
        {
            if (GamerAccountId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_ACCOUNT_ID", "GamerAccountId is required for Payment.");
            }

            if (Amount <= 0)
            {
                throw new InvalidDomainException("INVALID_AMOUNT", "Payment amount must be strictly greater than zero.");
            }

            if (string.IsNullOrWhiteSpace(Currency))
            {
                Currency = "SAY";
            }
            Currency = Currency.Trim().ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(IdempotencyKey))
            {
                throw new InvalidDomainException("INVALID_IDEMPOTENCY_KEY", "IdempotencyKey is required for Payment.");
            }
            IdempotencyKey = IdempotencyKey.Trim();

            if (string.IsNullOrWhiteSpace(PaymentMethod))
            {
                PaymentMethod = "ACCOUNT_BALANCE";
            }
            PaymentMethod = PaymentMethod.Trim().ToUpperInvariant();

            Status = (Status ?? string.Empty).Trim().ToUpperInvariant();
            if (Status != "PENDING" && Status != "COMPLETED" && Status != "FAILED" && Status != "CANCELLED")
            {
                throw new InvalidDomainException("INVALID_STATUS", $"Invalid payment status: {Status}");
            }

            Reference = (Reference ?? string.Empty).Trim();
            Description = Description?.Trim();

            if (CreatedAtUtc == default)
            {
                CreatedAtUtc = DateTime.UtcNow;
            }
        }

        public void MarkCompleted(Guid financialTransactionId)
        {
            if (Status != "PENDING")
            {
                throw new InvalidDomainException("INVALID_STATE_TRANSITION", $"Cannot complete payment in status '{Status}'. Only PENDING payments can be completed.");
            }

            if (financialTransactionId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_TRANSACTION_ID", "FinancialTransactionId is required to complete payment.");
            }

            Status = "COMPLETED";
            FinancialTransactionId = financialTransactionId;
            CompletedAtUtc = DateTime.UtcNow;
            UpdatedAt = DateTime.UtcNow;
        }

        public void MarkFailed(string reason)
        {
            if (Status != "PENDING")
            {
                throw new InvalidDomainException("INVALID_STATE_TRANSITION", $"Cannot fail payment in status '{Status}'.");
            }

            Status = "FAILED";
            FailureReason = reason?.Trim();
            UpdatedAt = DateTime.UtcNow;
        }
    }
}
