using System;
using Sayra.Backend.Domain.Events;
using Sayra.Backend.Domain.ValueObjects;

namespace Sayra.Backend.Domain.Entities
{
    public class CommunicationSession : BaseEntity
    {
        public string ConnectionId { get; private set; } = string.Empty;
        public string? PcId { get; private set; }
        public Guid? WorkstationId { get; private set; }
        public ConnectionLifecycleState State { get; private set; } = ConnectionLifecycleState.Connecting;

        public DateTime? LastHeartbeatAt { get; private set; }
        public DateTime LastActivityAt { get; private set; }
        public int MissedHeartbeats { get; private set; }
        public HeartbeatStatus HeartbeatStatus { get; private set; } = HeartbeatStatus.Healthy;

        public DateTime ConnectedAt { get; private set; }
        public DateTime? AuthenticatedAt { get; private set; }
        public DateTime? ActivatedAt { get; private set; }
        public DateTime? DisconnectedAt { get; private set; }
        public DateTime? TerminatedAt { get; private set; }

        public string? DisconnectReason { get; private set; }
        public string? RemoteIpAddress { get; private set; }
        public string? Hostname { get; private set; }

        public ConnectionId TypedConnectionId => new(ConnectionId);
        public CommunicationSessionId SessionId => new(Id);

        public HeartbeatState HeartbeatState => new(LastHeartbeatAt, LastActivityAt, MissedHeartbeats, HeartbeatStatus);

        public bool IsActive => State == ConnectionLifecycleState.Active
                             || State == ConnectionLifecycleState.Degraded
                             || State == ConnectionLifecycleState.Authenticated;

        public bool IsTerminated => State == ConnectionLifecycleState.Terminated
                                 || State == ConnectionLifecycleState.Disconnected;

        private CommunicationSession()
        {
            // Parameterless constructor for EF Core
        }

        public static CommunicationSession Create(
            ConnectionId connectionId,
            string? remoteIpAddress = null,
            string? hostname = null,
            DateTime? connectedAt = null)
        {
            var now = connectedAt ?? DateTime.UtcNow;
            var session = new CommunicationSession
            {
                Id = Guid.NewGuid(),
                ConnectionId = connectionId.Value,
                RemoteIpAddress = remoteIpAddress,
                Hostname = hostname,
                State = ConnectionLifecycleState.Connecting,
                ConnectedAt = now,
                LastActivityAt = now,
                HeartbeatStatus = HeartbeatStatus.Healthy,
                CreatedAt = now
            };

            session.AddDomainEvent(new ConnectionEstablishedEvent(
                session.Id,
                session.ConnectionId,
                remoteIpAddress,
                now));

            return session;
        }

        public void Authenticate(string pcId, Guid? workstationId = null, DateTime? timestamp = null)
        {
            var now = timestamp ?? DateTime.UtcNow;
            ConnectionLifecycleValidator.ValidateTransition(State, ConnectionLifecycleState.Authenticated);

            PcId = pcId;
            WorkstationId = workstationId;
            State = ConnectionLifecycleState.Authenticated;
            AuthenticatedAt = now;
            LastActivityAt = now;

            AddDomainEvent(new ConnectionAuthenticatedEvent(
                Id,
                ConnectionId,
                pcId,
                workstationId,
                now));
        }

        public void Activate(DateTime? timestamp = null)
        {
            var now = timestamp ?? DateTime.UtcNow;
            ConnectionLifecycleValidator.ValidateTransition(State, ConnectionLifecycleState.Active);

            State = ConnectionLifecycleState.Active;
            ActivatedAt = now;
            LastActivityAt = now;

            AddDomainEvent(new ConnectionActivatedEvent(
                Id,
                ConnectionId,
                PcId,
                now));
        }

        public void RecordHeartbeat(DateTime? timestamp = null, TimeSpan? heartbeatInterval = null)
        {
            var now = timestamp ?? DateTime.UtcNow;
            LastHeartbeatAt = now;
            LastActivityAt = now;
            MissedHeartbeats = 0;
            HeartbeatStatus = HeartbeatStatus.Healthy;

            if (State == ConnectionLifecycleState.Degraded)
            {
                State = ConnectionLifecycleState.Active;
            }

            AddDomainEvent(new HeartbeatReceivedEvent(
                Id,
                ConnectionId,
                PcId,
                now));
        }

        public void RecordActivity(DateTime? timestamp = null)
        {
            var now = timestamp ?? DateTime.UtcNow;
            if (now > LastActivityAt)
            {
                LastActivityAt = now;
            }
        }

        public void MarkDegraded(string? reason = null, DateTime? timestamp = null)
        {
            var now = timestamp ?? DateTime.UtcNow;
            ConnectionLifecycleValidator.ValidateTransition(State, ConnectionLifecycleState.Degraded);

            State = ConnectionLifecycleState.Degraded;
            HeartbeatStatus = HeartbeatStatus.Degraded;

            AddDomainEvent(new ConnectionDegradedEvent(
                Id,
                ConnectionId,
                PcId,
                reason,
                now));
        }

        public void Disconnect(string reason, DateTime? timestamp = null)
        {
            if (State == ConnectionLifecycleState.Disconnected || State == ConnectionLifecycleState.Terminated)
            {
                return;
            }

            var now = timestamp ?? DateTime.UtcNow;
            ConnectionLifecycleValidator.ValidateTransition(State, ConnectionLifecycleState.Disconnected);

            State = ConnectionLifecycleState.Disconnected;
            DisconnectedAt = now;
            DisconnectReason = reason;

            AddDomainEvent(new ConnectionDisconnectedEvent(
                Id,
                ConnectionId,
                PcId,
                reason,
                now));
        }

        public void Terminate(string reason, DateTime? timestamp = null)
        {
            if (State == ConnectionLifecycleState.Terminated)
            {
                return;
            }

            var now = timestamp ?? DateTime.UtcNow;
            ConnectionLifecycleValidator.ValidateTransition(State, ConnectionLifecycleState.Terminated);

            State = ConnectionLifecycleState.Terminated;
            TerminatedAt = now;
            DisconnectReason = reason;

            AddDomainEvent(new CommunicationSessionTerminatedEvent(
                Id,
                ConnectionId,
                PcId,
                reason,
                now));
        }
    }
}
