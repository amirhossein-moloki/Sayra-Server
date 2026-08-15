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
}
