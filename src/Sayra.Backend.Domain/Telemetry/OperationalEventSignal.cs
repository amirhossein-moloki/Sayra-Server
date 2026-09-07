using System;
using Sayra.Backend.Domain.ValueObjects;

namespace Sayra.Backend.Domain.Telemetry
{
    public sealed class OperationalEventSignal
    {
        public string EventId { get; }
        public string EventType { get; }
        public WorkstationIdentity Identity { get; }
        public string? SessionId { get; }
        public string CorrelationId { get; }
        public DateTime OccurredAt { get; }
        public string Payload { get; }
        public DateTime ServerReceivedAt { get; }
        public DateTime ProcessedAt { get; }

        public OperationalEventSignal(
            string eventId,
            string eventType,
            WorkstationIdentity identity,
            string? sessionId,
            string correlationId,
            DateTime occurredAt,
            string payload,
            DateTime serverReceivedAt,
            DateTime processedAt)
        {
            if (string.IsNullOrWhiteSpace(eventId))
            {
                throw new ArgumentException("EventId cannot be null or empty.", nameof(eventId));
            }

            if (string.IsNullOrWhiteSpace(eventType))
            {
                throw new ArgumentException("EventType cannot be null or empty.", nameof(eventType));
            }

            EventId = eventId.Trim();
            EventType = eventType.Trim().ToUpperInvariant();
            Identity = identity ?? throw new ArgumentNullException(nameof(identity));
            SessionId = sessionId;
            CorrelationId = correlationId ?? string.Empty;
            OccurredAt = occurredAt;
            Payload = payload ?? "{}";
            ServerReceivedAt = serverReceivedAt;
            ProcessedAt = processedAt;
        }
    }
}
