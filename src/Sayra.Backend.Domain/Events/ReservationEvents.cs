using System;

namespace Sayra.Backend.Domain.Events
{
    public record ReservationCreated(
        Guid ReservationId,
        Guid GamerId,
        Guid OrganizationId,
        Guid SiteId,
        Guid? WorkstationId,
        Guid? ZoneId,
        DateTime StartTimeUtc,
        DateTime EndTimeUtc,
        string Status,
        decimal ReservedAmount,
        DateTime OccurredOn,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), OccurredOn, CorrelationId);

    public record ReservationConfirmed(
        Guid ReservationId,
        Guid GamerId,
        Guid SiteId,
        string Status,
        DateTime OccurredOn,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), OccurredOn, CorrelationId);

    public record ReservationCancelled(
        Guid ReservationId,
        Guid GamerId,
        Guid SiteId,
        string Status,
        string Reason,
        DateTime OccurredOn,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), OccurredOn, CorrelationId);

    public record ReservationActivated(
        Guid ReservationId,
        Guid GamerId,
        Guid SiteId,
        Guid? WorkstationId,
        string Status,
        DateTime OccurredOn,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), OccurredOn, CorrelationId);

    public record ReservationExpired(
        Guid ReservationId,
        Guid GamerId,
        Guid SiteId,
        string Status,
        DateTime OccurredOn,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), OccurredOn, CorrelationId);

    public record ReservationCompleted(
        Guid ReservationId,
        Guid GamerId,
        Guid SiteId,
        string Status,
        DateTime OccurredOn,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), OccurredOn, CorrelationId);
}
