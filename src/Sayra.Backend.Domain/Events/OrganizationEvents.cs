using System;

namespace Sayra.Backend.Domain.Events
{
    public record OrganizationCreated(
        Guid OrganizationId,
        string Code,
        string Name,
        string Status,
        DateTime OccurredOn,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), OccurredOn, CorrelationId);

    public record OrganizationDeactivated(
        Guid OrganizationId,
        string Code,
        string Status,
        DateTime OccurredOn,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), OccurredOn, CorrelationId);
}
