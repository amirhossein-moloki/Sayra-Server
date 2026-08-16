using System;
using Sayra.Backend.Domain.Exceptions;

namespace Sayra.Backend.Domain
{
    public class SessionSegment : BaseEntity
    {
        public Guid SessionId { get; set; }
        public string Type { get; set; } = "ACTIVE";
        public DateTime StartedAtUtc { get; set; }
        public DateTime? EndedAtUtc { get; set; }

        public Guid SessionSegmentId
        {
            get => Id;
            set => Id = value;
        }

        public void NormalizeAndValidate()
        {
            if (SessionId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_SESSION_ID", "SessionId is required for SessionSegment.");
            }

            var typeTrimmed = (Type ?? string.Empty).Trim().ToUpperInvariant();
            if (typeTrimmed != "ACTIVE" && typeTrimmed != "PAUSED")
            {
                throw new InvalidDomainException("INVALID_SEGMENT_TYPE", $"Invalid SessionSegment type: {Type}");
            }

            Type = typeTrimmed;
            StartedAtUtc = StartedAtUtc.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(StartedAtUtc, DateTimeKind.Utc)
                : StartedAtUtc.ToUniversalTime();

            if (EndedAtUtc.HasValue)
            {
                EndedAtUtc = EndedAtUtc.Value.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(EndedAtUtc.Value, DateTimeKind.Utc)
                    : EndedAtUtc.Value.ToUniversalTime();

                if (EndedAtUtc.Value < StartedAtUtc)
                {
                    throw new InvalidDomainException("INVALID_TIME_RANGE", "EndedAtUtc cannot be earlier than StartedAtUtc.");
                }
            }
        }

        public TimeSpan GetDuration(DateTime currentServerTimeUtc)
        {
            var end = EndedAtUtc ?? currentServerTimeUtc;
            if (end < StartedAtUtc)
            {
                return TimeSpan.Zero;
            }
            return end - StartedAtUtc;
        }
    }
}
