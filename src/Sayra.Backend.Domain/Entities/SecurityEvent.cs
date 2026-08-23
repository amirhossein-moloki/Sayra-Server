using System;
using Sayra.Backend.Domain.Exceptions;

namespace Sayra.Backend.Domain.Entities
{
    public class SecurityEvent : BaseEntity
    {
        public Guid SecurityEventId
        {
            get => Id;
            set => Id = value;
        }

        public string EventType { get; set; } = string.Empty;
        public Guid? ActorId { get; set; }
        public string? ActorType { get; set; }
        public string? DeviceId { get; set; }
        public Guid? OrganizationId { get; set; }
        public Guid? SiteId { get; set; }
        public string? ResourceType { get; set; }
        public Guid? ResourceId { get; set; }
        public string? Action { get; set; }
        public string Result { get; set; } = "SUCCESS";
        public string? FailureReason { get; set; }
        public string? CorrelationId { get; set; }
        public string? TraceId { get; set; }

        public void NormalizeAndValidate()
        {
            if (string.IsNullOrWhiteSpace(EventType))
            {
                throw new InvalidDomainException("INVALID_EVENT_TYPE", "EventType is required for SecurityEvent.");
            }

            EventType = EventType.Trim().ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(Result))
            {
                Result = "SUCCESS";
            }
            else
            {
                Result = Result.Trim().ToUpperInvariant();
            }

            if (Id == Guid.Empty)
            {
                Id = Guid.NewGuid();
            }

            if (CreatedAt == default)
            {
                CreatedAt = DateTime.UtcNow;
            }
        }
    }
}
