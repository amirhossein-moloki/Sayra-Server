using System;

namespace Sayra.Backend.Domain.Events
{
    public record SiteCreated(
        Guid SiteId,
        Guid OrganizationId,
        string Code,
        string Name,
        string Status,
        string Timezone,
        DateTime OccurredOn,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), OccurredOn, CorrelationId);

    public record SiteDeactivated(
        Guid SiteId,
        Guid OrganizationId,
        string Code,
        string Status,
        DateTime OccurredOn,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), OccurredOn, CorrelationId);

    public record ZoneCreated(
        Guid ZoneId,
        Guid SiteId,
        string Code,
        string Name,
        string Status,
        DateTime OccurredOn,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), OccurredOn, CorrelationId);

    public record ZoneDeactivated(
        Guid ZoneId,
        Guid SiteId,
        string Code,
        string Status,
        DateTime OccurredOn,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), OccurredOn, CorrelationId);
}
