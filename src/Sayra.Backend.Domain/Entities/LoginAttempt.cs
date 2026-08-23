using System;
using Sayra.Backend.Domain.Exceptions;

namespace Sayra.Backend.Domain.Entities
{
    public class LoginAttempt : BaseEntity
    {
        public Guid LoginAttemptId
        {
            get => Id;
            set => Id = value;
        }

        public string UsernameIdentifier { get; set; } = string.Empty;
        public Guid? UserId { get; set; }
        public string? IpAddress { get; set; }
        public string? DeviceId { get; set; }
        public bool Success { get; set; }
        public string? FailureReason { get; set; }
        public int AttemptCount { get; set; } = 0;
        public DateTime LastAttemptAt { get; set; } = DateTime.UtcNow;
        public DateTime? LockedUntil { get; set; }

        public void NormalizeAndValidate()
        {
            if (string.IsNullOrWhiteSpace(UsernameIdentifier))
            {
                throw new InvalidDomainException("INVALID_USERNAME_IDENTIFIER", "UsernameIdentifier is required for LoginAttempt.");
            }

            UsernameIdentifier = UsernameIdentifier.Trim().ToLowerInvariant();

            if (Id == Guid.Empty)
            {
                Id = Guid.NewGuid();
            }

            if (CreatedAt == default)
            {
                CreatedAt = DateTime.UtcNow;
            }

            if (LastAttemptAt == default)
            {
                LastAttemptAt = DateTime.UtcNow;
            }
        }
    }
}
