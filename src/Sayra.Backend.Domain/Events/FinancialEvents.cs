// PHASE 03 — STAGE 03-09: Payment / Financial Transaction Engine & Idempotency
using System;

namespace Sayra.Backend.Domain.Events
{
    public record FinancialTransactionCreated(
        Guid TransactionId,
        Guid GamerAccountId,
        decimal Amount,
        string Currency,
        string OperationType,
        string IdempotencyKey,
        DateTime Timestamp,
        string CorrelationId
    );

    public record FinancialTransactionCompleted(
        Guid TransactionId,
        Guid GamerAccountId,
        Guid LedgerEntryId,
        decimal Amount,
        string Currency,
        string OperationType,
        DateTime Timestamp,
        string CorrelationId
    );

    public record FinancialTransactionFailed(
        Guid TransactionId,
        Guid GamerAccountId,
        string Reason,
        DateTime Timestamp,
        string CorrelationId
    );

    public record FinancialTransactionCancelled(
        Guid TransactionId,
        Guid GamerAccountId,
        string Reason,
        DateTime Timestamp,
        string CorrelationId
    );

    public record FinancialTransactionReversed(
        Guid OriginalTransactionId,
        Guid ReversalTransactionId,
        Guid GamerAccountId,
        decimal Amount,
        string Currency,
        DateTime Timestamp,
        string CorrelationId
    );

    public record PaymentCreated(
        Guid PaymentId,
        Guid GamerAccountId,
        decimal Amount,
        string Currency,
        string PaymentMethod,
        string IdempotencyKey,
        DateTime Timestamp,
        string CorrelationId
    );

    public record PaymentCompleted(
        Guid PaymentId,
        Guid GamerAccountId,
        Guid FinancialTransactionId,
        decimal Amount,
        string Currency,
        DateTime Timestamp,
        string CorrelationId
    );

    public record PaymentFailed(
        Guid PaymentId,
        Guid GamerAccountId,
        string Reason,
        DateTime Timestamp,
        string CorrelationId
    );
}
