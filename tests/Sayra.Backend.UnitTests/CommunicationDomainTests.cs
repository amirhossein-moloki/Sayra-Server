using System;
using System.Linq;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Domain.Events;
using Sayra.Backend.Domain.ValueObjects;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class CommunicationDomainTests
    {
        [Fact]
        public void ConnectionId_ValidValue_CreatesAndConvertsCorrectly()
        {
            var raw = "CONN-ALPHA-123";
            var id = new ConnectionId(raw);

            Assert.Equal(raw, id.Value);
            Assert.Equal(raw, id.ToString());

            string implicitStr = id;
            Assert.Equal(raw, implicitStr);

            ConnectionId implicitId = raw;
            Assert.Equal(id, implicitId);
        }

        [Fact]
        public void ConnectionId_NullOrWhitespace_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new ConnectionId(""));
            Assert.Throws<ArgumentException>(() => new ConnectionId("   "));
        }

        [Fact]
        public void CommunicationSessionId_ValidGuid_CreatesAndParsesCorrectly()
        {
            var guid = Guid.NewGuid();
            var sessionId = new CommunicationSessionId(guid);

            Assert.Equal(guid, sessionId.Value);
            Assert.Equal(guid.ToString(), sessionId.ToString());

            var parsed = CommunicationSessionId.Parse(guid.ToString());
            Assert.Equal(sessionId, parsed);
        }

        [Fact]
        public void CommunicationSessionId_EmptyGuid_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new CommunicationSessionId(Guid.Empty));
        }

        [Fact]
        public void MessageId_ValidValue_CreatesAndConvertsCorrectly()
        {
            var msgStr = "MSG-999";
            var msgId = new MessageId(msgStr);

            Assert.Equal(msgStr, msgId.Value);
            Assert.Equal(msgStr, msgId.ToString());
        }

        [Fact]
        public void HeartbeatState_LivenessEvaluation_TransitionsStatusCorrectly()
        {
            var now = DateTime.UtcNow;
            var heartbeatState = HeartbeatState.Initial(now);

            Assert.Equal(HeartbeatStatus.Healthy, heartbeatState.Status);
            Assert.Equal(0, heartbeatState.MissedHeartbeats);

            // Within thresholds -> Healthy
            var evaluatedHealthy = heartbeatState.EvaluateLiveness(
                now.AddSeconds(10),
                timeoutThreshold: TimeSpan.FromSeconds(60),
                degradedThreshold: TimeSpan.FromSeconds(30));
            Assert.Equal(HeartbeatStatus.Healthy, evaluatedHealthy.Status);

            // Exceeding degraded threshold -> Degraded
            var evaluatedDegraded = heartbeatState.EvaluateLiveness(
                now.AddSeconds(35),
                timeoutThreshold: TimeSpan.FromSeconds(60),
                degradedThreshold: TimeSpan.FromSeconds(30));
            Assert.Equal(HeartbeatStatus.Degraded, evaluatedDegraded.Status);

            // Exceeding timeout threshold -> TimedOut
            var evaluatedTimedOut = heartbeatState.EvaluateLiveness(
                now.AddSeconds(65),
                timeoutThreshold: TimeSpan.FromSeconds(60),
                degradedThreshold: TimeSpan.FromSeconds(30));
            Assert.Equal(HeartbeatStatus.TimedOut, evaluatedTimedOut.Status);
            Assert.Equal(1, evaluatedTimedOut.MissedHeartbeats);
        }

        [Fact]
        public void CommunicationSession_Create_InitializesConnectingStateAndEmitsEvent()
        {
            var connId = ConnectionId.New();
            var now = DateTime.UtcNow;

            var session = CommunicationSession.Create(connId, "192.168.1.50", "HOST-50", now);

            Assert.NotEqual(Guid.Empty, session.Id);
            Assert.Equal(connId.Value, session.ConnectionId);
            Assert.Equal(ConnectionLifecycleState.Connecting, session.State);
            Assert.Equal("192.168.1.50", session.RemoteIpAddress);
            Assert.Equal("HOST-50", session.Hostname);
            Assert.Equal(now, session.ConnectedAt);

            var domainEvent = session.DomainEvents.OfType<ConnectionEstablishedEvent>().FirstOrDefault();
            Assert.NotNull(domainEvent);
            Assert.Equal(session.Id, domainEvent.SessionId);
            Assert.Equal(connId.Value, domainEvent.ConnectionId);
        }

        [Fact]
        public void CommunicationSession_FullLifecycle_TransitionsAndEmitsEventsCorrectly()
        {
            var connId = new ConnectionId("CONN-TEST-1");
            var workstationId = Guid.NewGuid();
            var pcId = "PC-LAB-01";

            var session = CommunicationSession.Create(connId);
            Assert.Equal(ConnectionLifecycleState.Connecting, session.State);

            // 1. Authenticate
            session.Authenticate(pcId, workstationId);
            Assert.Equal(ConnectionLifecycleState.Authenticated, session.State);
            Assert.Equal(pcId, session.PcId);
            Assert.Equal(workstationId, session.WorkstationId);
            Assert.NotNull(session.AuthenticatedAt);
            Assert.Contains(session.DomainEvents, e => e is ConnectionAuthenticatedEvent);

            // 2. Activate
            session.Activate();
            Assert.Equal(ConnectionLifecycleState.Active, session.State);
            Assert.NotNull(session.ActivatedAt);
            Assert.Contains(session.DomainEvents, e => e is ConnectionActivatedEvent);

            // 3. Heartbeat
            session.RecordHeartbeat();
            Assert.NotNull(session.LastHeartbeatAt);
            Assert.Equal(HeartbeatStatus.Healthy, session.HeartbeatStatus);
            Assert.Contains(session.DomainEvents, e => e is HeartbeatReceivedEvent);

            // 4. Mark Degraded
            session.MarkDegraded("Network Delay");
            Assert.Equal(ConnectionLifecycleState.Degraded, session.State);
            Assert.Equal(HeartbeatStatus.Degraded, session.HeartbeatStatus);
            Assert.Contains(session.DomainEvents, e => e is ConnectionDegradedEvent);

            // 5. Heartbeat recovers degraded session to Active
            session.RecordHeartbeat();
            Assert.Equal(ConnectionLifecycleState.Active, session.State);

            // 6. Disconnect
            session.Disconnect("Client Shutdown");
            Assert.Equal(ConnectionLifecycleState.Disconnected, session.State);
            Assert.NotNull(session.DisconnectedAt);
            Assert.Equal("Client Shutdown", session.DisconnectReason);
            Assert.Contains(session.DomainEvents, e => e is ConnectionDisconnectedEvent);

            // 7. Terminate
            session.Terminate("Administrative Cleanup");
            Assert.Equal(ConnectionLifecycleState.Terminated, session.State);
            Assert.NotNull(session.TerminatedAt);
            Assert.Contains(session.DomainEvents, e => e is CommunicationSessionTerminatedEvent);
        }

        [Fact]
        public void CommunicationSession_InvalidTransitionFromTerminated_ThrowsInvalidOperationException()
        {
            var session = CommunicationSession.Create("CONN-TERM-TEST");
            session.Disconnect("Shutdown");
            session.Terminate("Force Cleanup");

            Assert.Throws<InvalidOperationException>(() => session.Activate());
        }
    }
}
