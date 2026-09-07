using System;
using Sayra.Backend.Domain.ValueObjects;

namespace Sayra.Backend.Domain.Telemetry
{
    public sealed class HeartbeatSignal
    {
        public WorkstationIdentity Identity { get; }
        public DateTime ClientTimestamp { get; }
        public DateTime ServerReceivedAt { get; }
        public DateTime ProcessedAt { get; }

        public HeartbeatSignal(
            WorkstationIdentity identity,
            DateTime clientTimestamp,
            DateTime serverReceivedAt,
            DateTime processedAt)
        {
            Identity = identity ?? throw new ArgumentNullException(nameof(identity));
            ClientTimestamp = clientTimestamp;
            ServerReceivedAt = serverReceivedAt;
            ProcessedAt = processedAt;
        }
    }
}
