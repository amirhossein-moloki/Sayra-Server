using System;

namespace Sayra.Backend.Domain.Events
{
    // Legacy / Infrastructure events preserved for backward compatibility
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

    // Phase 05 Domain Lifecycle Events
    public record ConnectionEstablishedEvent(
        Guid SessionId,
        string ConnectionId,
        string? RemoteIpAddress,
        DateTime ConnectedAt,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), ConnectedAt, CorrelationId);

    public record ConnectionAuthenticatedEvent(
        Guid SessionId,
        string ConnectionId,
        string PcId,
        Guid? WorkstationId,
        DateTime AuthenticatedAt,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), AuthenticatedAt, CorrelationId);

    public record ConnectionActivatedEvent(
        Guid SessionId,
        string ConnectionId,
        string? PcId,
        DateTime ActivatedAt,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), ActivatedAt, CorrelationId);

    public record HeartbeatReceivedEvent(
        Guid SessionId,
        string ConnectionId,
        string? PcId,
        DateTime HeartbeatAt,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), HeartbeatAt, CorrelationId);

    public record ConnectionDegradedEvent(
        Guid SessionId,
        string ConnectionId,
        string? PcId,
        string? Reason,
        DateTime DegradedAt,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), DegradedAt, CorrelationId);

    public record ConnectionDisconnectedEvent(
        Guid SessionId,
        string ConnectionId,
        string? PcId,
        string Reason,
        DateTime DisconnectedAt,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), DisconnectedAt, CorrelationId);

    public record CommunicationSessionTerminatedEvent(
        Guid SessionId,
        string ConnectionId,
        string? PcId,
        string Reason,
        DateTime TerminatedAt,
        string CorrelationId = "") : DomainEvent(Guid.NewGuid(), TerminatedAt, CorrelationId);
}
