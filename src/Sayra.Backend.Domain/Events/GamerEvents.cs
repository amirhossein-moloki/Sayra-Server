using System;

namespace Sayra.Backend.Domain.Events
{
    public record GamerCreated(
        Guid GamerEntityId,
        string GamerId,
        string Username,
        string Email,
        string Status,
        DateTime OccurredOn,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), OccurredOn, CorrelationId);

    public record GamerDeactivated(
        Guid GamerEntityId,
        string GamerId,
        string Username,
        string Status,
        DateTime OccurredOn,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), OccurredOn, CorrelationId);

    public record GamerStatusChanged(
        Guid GamerEntityId,
        string GamerId,
        string OldStatus,
        string NewStatus,
        DateTime OccurredOn,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), OccurredOn, CorrelationId);

    public record GamerPasswordChanged(
        Guid GamerEntityId,
        string GamerId,
        DateTime OccurredOn,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), OccurredOn, CorrelationId);

    public record GamerAccountCreated(
        Guid GamerAccountEntityId,
        Guid GamerEntityId,
        string AccountNumber,
        string Status,
        decimal InitialBalance,
        DateTime OccurredOn,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), OccurredOn, CorrelationId);

    public record GamerAccountStatusChanged(
        Guid GamerAccountEntityId,
        Guid GamerEntityId,
        string AccountNumber,
        string OldStatus,
        string NewStatus,
        DateTime OccurredOn,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), OccurredOn, CorrelationId);

    public record BalanceCredited(
        Guid GamerAccountId,
        decimal Amount,
        string Currency,
        string Reference,
        decimal NewBalance,
        DateTime OccurredOn,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), OccurredOn, CorrelationId);

    public record BalanceDebited(
        Guid GamerAccountId,
        decimal Amount,
        string Currency,
        string Reference,
        decimal NewBalance,
        DateTime OccurredOn,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), OccurredOn, CorrelationId);

    public record LedgerEntryCreated(
        Guid LedgerEntryId,
        Guid GamerAccountId,
        decimal Amount,
        string Currency,
        string Direction,
        string EntryType,
        string Reference,
        decimal BalanceAfter,
        DateTime OccurredOn,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), OccurredOn, CorrelationId);

    public record BalanceChanged(
        Guid GamerAccountId,
        decimal OldBalance,
        decimal NewBalance,
        string Currency,
        string Reason,
        DateTime OccurredOn,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), OccurredOn, CorrelationId);
}
