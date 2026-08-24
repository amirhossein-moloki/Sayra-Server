using System;

namespace Sayra.Backend.Domain.Events
{
    public record ClientConnectedEvent(
        string ConnectionId,
        string RemoteIpAddress,
        DateTime ConnectedAt,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), ConnectedAt, CorrelationId);

    public record ClientDisconnectedEvent(
        string ConnectionId,
        string? PcId,
        string RemoteIpAddress,
        string DisconnectReason,
        DateTime DisconnectedAt,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), DisconnectedAt, CorrelationId);

    public record ClientConnectionStateChangedEvent(
        string ConnectionId,
        string? PcId,
        string OldState,
        string NewState,
        DateTime Timestamp,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), Timestamp, CorrelationId);
}
