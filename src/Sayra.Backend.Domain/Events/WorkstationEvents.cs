using System;

namespace Sayra.Backend.Domain.Events
{
    public record WorkstationAssigned(
        Guid WorkstationId,
        string PcId,
        Guid OrganizationId,
        Guid SiteId,
        Guid ZoneId,
        DateTime OccurredOn,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), OccurredOn, CorrelationId);

    public record WorkstationAssignmentChanged(
        Guid WorkstationId,
        string PcId,
        Guid OrganizationId,
        Guid SiteId,
        Guid ZoneId,
        Guid? PreviousSiteId,
        Guid? PreviousZoneId,
        DateTime OccurredOn,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), OccurredOn, CorrelationId);
}
