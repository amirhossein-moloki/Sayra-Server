using System;

namespace Sayra.Backend.Application.Sessions
{
    public class SessionTimingSnapshot
    {
        public Guid SessionId { get; set; }
        public DateTime CurrentServerTimeUtc { get; set; }
        public DateTime StartedAtUtc { get; set; }
        public TimeSpan ConsumedDuration { get; set; }
        public TimeSpan PausedDuration { get; set; }
        public TimeSpan? RemainingDuration { get; set; }
        public DateTime? ExpirationTimeUtc { get; set; }
    }
}
