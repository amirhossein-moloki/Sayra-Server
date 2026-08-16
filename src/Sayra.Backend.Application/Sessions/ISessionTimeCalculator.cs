using System;
using System.Collections.Generic;
using System.Linq;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Application.Sessions
{
    public interface ISessionTimeCalculator
    {
        SessionTimingSnapshot CalculateTiming(
            Session session,
            IEnumerable<SessionSegment> segments,
            DateTime serverTimeUtc,
            TimeSpan? allocatedDuration = null);
    }

    public class SessionTimeCalculator : ISessionTimeCalculator
    {
        public SessionTimingSnapshot CalculateTiming(
            Session session,
            IEnumerable<SessionSegment> segments,
            DateTime serverTimeUtc,
            TimeSpan? allocatedDuration = null)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            var utcNow = serverTimeUtc.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(serverTimeUtc, DateTimeKind.Utc)
                : serverTimeUtc.ToUniversalTime();

            var segmentList = (segments ?? Array.Empty<SessionSegment>()).ToList();

            TimeSpan activeDuration = TimeSpan.Zero;
            TimeSpan pausedDuration = TimeSpan.Zero;

            foreach (var segment in segmentList)
            {
                var duration = segment.GetDuration(utcNow);
                var type = (segment.Type ?? string.Empty).Trim().ToUpperInvariant();
                if (type == "ACTIVE")
                {
                    activeDuration += duration;
                }
                else if (type == "PAUSED")
                {
                    pausedDuration += duration;
                }
            }

            TimeSpan? remainingDuration = null;
            DateTime? expirationTimeUtc = null;

            if (allocatedDuration.HasValue)
            {
                var remaining = allocatedDuration.Value - activeDuration;
                if (remaining < TimeSpan.Zero)
                {
                    remaining = TimeSpan.Zero;
                }
                remainingDuration = remaining;

                var currentStatus = (session.Status ?? string.Empty).Trim().ToUpperInvariant();
                if (currentStatus == "ACTIVE" || currentStatus == "PAUSED" || currentStatus == "STARTING" || currentStatus == "ENDING" || currentStatus == "IDLE")
                {
                    expirationTimeUtc = utcNow.Add(remaining);
                }
            }

            return new SessionTimingSnapshot
            {
                SessionId = session.Id,
                CurrentServerTimeUtc = utcNow,
                StartedAtUtc = session.StartedAt,
                ConsumedDuration = activeDuration,
                PausedDuration = pausedDuration,
                RemainingDuration = remainingDuration,
                ExpirationTimeUtc = expirationTimeUtc
            };
        }
    }
}
