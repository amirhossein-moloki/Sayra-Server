using System;

namespace Sayra.Backend.Domain.Events
{
    public record SessionCreated(
        Guid SessionId,
        Guid GamerId,
        Guid WorkstationId,
        Guid OrganizationId,
        Guid SiteId,
        Guid? ReservationId,
        string Status,
        DateTime StartedAt,
        DateTime OccurredOn,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), OccurredOn, CorrelationId);

    public record SessionStarted(
        Guid SessionId,
        Guid GamerId,
        Guid WorkstationId,
        Guid SiteId,
        string Status,
        DateTime OccurredOn,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), OccurredOn, CorrelationId);

    public record SessionPaused(
        Guid SessionId,
        Guid GamerId,
        Guid WorkstationId,
        string Status,
        DateTime OccurredOn,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), OccurredOn, CorrelationId);

    public record SessionResumed(
        Guid SessionId,
        Guid GamerId,
        Guid WorkstationId,
        string Status,
        DateTime OccurredOn,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), OccurredOn, CorrelationId);

    public record SessionStopped(
        Guid SessionId,
        Guid GamerId,
        Guid WorkstationId,
        string Status,
        DateTime OccurredOn,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), OccurredOn, CorrelationId);

    public record SessionCancelled(
        Guid SessionId,
        Guid GamerId,
        Guid WorkstationId,
        string Status,
        DateTime OccurredOn,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), OccurredOn, CorrelationId);

    public record SessionTerminated(
        Guid SessionId,
        Guid GamerId,
        Guid WorkstationId,
        string Status,
        string Reason,
        DateTime OccurredOn,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), OccurredOn, CorrelationId);

    public record SessionExtended(
        Guid SessionId,
        Guid GamerId,
        Guid WorkstationId,
        TimeSpan ExtendedDuration,
        decimal Cost,
        DateTime OccurredOn,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), OccurredOn, CorrelationId);

    public record SessionExpired(
        Guid SessionId,
        Guid GamerId,
        Guid WorkstationId,
        string Status,
        DateTime OccurredOn,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), OccurredOn, CorrelationId);
}
