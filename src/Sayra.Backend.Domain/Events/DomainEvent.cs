using System;

namespace Sayra.Backend.Domain.Events
{
    public interface IDomainEvent
    {
        Guid EventId { get; }
        DateTime OccurredOn { get; }
        string CorrelationId { get; }
    }

    public abstract record DomainEvent(Guid EventId, DateTime OccurredOn, string CorrelationId) : IDomainEvent
    {
        protected DomainEvent(string correlationId = "")
            : this(Guid.NewGuid(), DateTime.UtcNow, correlationId)
        {
        }
    }
}
