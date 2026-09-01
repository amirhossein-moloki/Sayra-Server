using System;

namespace Sayra.Backend.Domain.ValueObjects
{
    public enum HeartbeatStatus
    {
        Healthy = 0,
        Degraded = 1,
        TimedOut = 2
    }

    public readonly record struct HeartbeatState
    {
        public DateTime? LastHeartbeatAt { get; }
        public DateTime LastActivityAt { get; }
        public int MissedHeartbeats { get; }
        public HeartbeatStatus Status { get; }

        public HeartbeatState(DateTime? lastHeartbeatAt, DateTime lastActivityAt, int missedHeartbeats = 0, HeartbeatStatus status = HeartbeatStatus.Healthy)
        {
            LastHeartbeatAt = lastHeartbeatAt;
            LastActivityAt = lastActivityAt;
            MissedHeartbeats = Math.Max(0, missedHeartbeats);
            Status = status;
        }

        public static HeartbeatState Initial(DateTime now) => new(null, now, 0, HeartbeatStatus.Healthy);

        public HeartbeatState RecordHeartbeat(DateTime timestamp)
        {
            return new HeartbeatState(
                lastHeartbeatAt: timestamp,
                lastActivityAt: timestamp > LastActivityAt ? timestamp : LastActivityAt,
                missedHeartbeats: 0,
                status: HeartbeatStatus.Healthy
            );
        }

        public HeartbeatState RecordActivity(DateTime timestamp)
        {
            return new HeartbeatState(
                lastHeartbeatAt: LastHeartbeatAt,
                lastActivityAt: timestamp > LastActivityAt ? timestamp : LastActivityAt,
                missedHeartbeats: MissedHeartbeats,
                status: Status
            );
        }

        public HeartbeatState EvaluateLiveness(DateTime now, TimeSpan timeoutThreshold, TimeSpan? degradedThreshold = null)
        {
            var referenceTime = LastHeartbeatAt ?? LastActivityAt;
            var elapsed = now - referenceTime;

            if (elapsed >= timeoutThreshold)
            {
                return new HeartbeatState(LastHeartbeatAt, LastActivityAt, MissedHeartbeats + 1, HeartbeatStatus.TimedOut);
            }

            if (degradedThreshold.HasValue && elapsed >= degradedThreshold.Value)
            {
                return new HeartbeatState(LastHeartbeatAt, LastActivityAt, MissedHeartbeats, HeartbeatStatus.Degraded);
            }

            return new HeartbeatState(LastHeartbeatAt, LastActivityAt, 0, HeartbeatStatus.Healthy);
        }
    }
}
