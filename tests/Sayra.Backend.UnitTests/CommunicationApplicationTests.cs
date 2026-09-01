using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Communication;
using Sayra.Backend.Application.Communication;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Domain.ValueObjects;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class CommunicationApplicationTests
    {
        private class InMemoryCommunicationSessionRepository : ICommunicationSessionRepository
        {
            private readonly Dictionary<Guid, CommunicationSession> _byId = new();
            private readonly Dictionary<string, CommunicationSession> _byConnectionId = new();
            private readonly Dictionary<string, CommunicationSession> _byPcId = new();

            public Task AddAsync(CommunicationSession session, CancellationToken cancellationToken = default)
            {
                _byId[session.Id] = session;
                _byConnectionId[session.ConnectionId] = session;
                if (!string.IsNullOrEmpty(session.PcId))
                {
                    _byPcId[session.PcId] = session;
                }
                return Task.CompletedTask;
            }

            public Task UpdateAsync(CommunicationSession session, CancellationToken cancellationToken = default)
            {
                _byId[session.Id] = session;
                _byConnectionId[session.ConnectionId] = session;
                if (!string.IsNullOrEmpty(session.PcId))
                {
                    _byPcId[session.PcId] = session;
                }
                return Task.CompletedTask;
            }

            public Task<CommunicationSession?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            {
                _byId.TryGetValue(id, out var session);
                return Task.FromResult(session);
            }

            public Task<CommunicationSession?> GetByConnectionIdAsync(string connectionId, CancellationToken cancellationToken = default)
            {
                _byConnectionId.TryGetValue(connectionId, out var session);
                return Task.FromResult(session);
            }

            public Task<CommunicationSession?> GetByPcIdAsync(string pcId, CancellationToken cancellationToken = default)
            {
                _byPcId.TryGetValue(pcId, out var session);
                return Task.FromResult(session);
            }

            public Task<CommunicationSession?> GetByWorkstationIdAsync(Guid workstationId, CancellationToken cancellationToken = default)
            {
                foreach (var s in _byId.Values)
                {
                    if (s.WorkstationId == workstationId) return Task.FromResult<CommunicationSession?>(s);
                }
                return Task.FromResult<CommunicationSession?>(null);
            }

            public Task<IReadOnlyList<CommunicationSession>> GetActiveSessionsAsync(CancellationToken cancellationToken = default)
            {
                IReadOnlyList<CommunicationSession> active = _byId.Values
                    .Where(s => s.State == ConnectionLifecycleState.Active
                             || s.State == ConnectionLifecycleState.Degraded
                             || s.State == ConnectionLifecycleState.Authenticated)
                    .ToList();
                return Task.FromResult(active);
            }
        }

        [Fact]
        public async Task EstablishConnection_CommandHandler_CreatesSessionAndReturnsDto()
        {
            var repo = new InMemoryCommunicationSessionRepository();
            var handler = new EstablishConnectionCommandHandler(repo);

            var cmd = new EstablishConnectionCommand("CONN-APP-100", "10.0.0.5", "WORKSTATION-5");
            var result = await handler.HandleAsync(cmd);

            Assert.True(result.IsSuccess);
            Assert.Equal("CONN-APP-100", result.Value!.ConnectionId);
            Assert.Equal("10.0.0.5", result.Value.RemoteIpAddress);
            Assert.Equal("Connecting", result.Value.State);
        }

        [Fact]
        public async Task FullLifecycle_CommandHandlers_ExecuteSuccessfully()
        {
            var repo = new InMemoryCommunicationSessionRepository();

            var establishHandler = new EstablishConnectionCommandHandler(repo);
            var authHandler = new AuthenticateConnectionCommandHandler(repo);
            var activateHandler = new ActivateConnectionCommandHandler(repo);
            var heartbeatHandler = new ProcessHeartbeatCommandHandler(repo);
            var disconnectHandler = new DisconnectConnectionCommandHandler(repo);
            var terminateHandler = new TerminateCommunicationSessionCommandHandler(repo);

            var connId = "CONN-LIFECYCLE-APP";
            var pcId = "PC-APP-01";
            var wsId = Guid.NewGuid();

            // 1. Establish
            var establishRes = await establishHandler.HandleAsync(new EstablishConnectionCommand(connId));
            Assert.True(establishRes.IsSuccess);
            Assert.Equal("Connecting", establishRes.Value!.State);

            // 2. Authenticate
            var authRes = await authHandler.HandleAsync(new AuthenticateConnectionCommand(connId, pcId, wsId));
            Assert.True(authRes.IsSuccess);
            Assert.Equal("Authenticated", authRes.Value!.State);
            Assert.Equal(pcId, authRes.Value.PcId);
            Assert.Equal(wsId, authRes.Value.WorkstationId);

            // 3. Activate
            var activateRes = await activateHandler.HandleAsync(new ActivateConnectionCommand(connId));
            Assert.True(activateRes.IsSuccess);
            Assert.Equal("Active", activateRes.Value!.State);

            // 4. Heartbeat
            var hbRes = await heartbeatHandler.HandleAsync(new ProcessHeartbeatCommand(connId));
            Assert.True(hbRes.IsSuccess);
            Assert.NotNull(hbRes.Value!.LastHeartbeatAt);

            // 5. Disconnect
            var discRes = await disconnectHandler.HandleAsync(new DisconnectConnectionCommand(connId, "User logout"));
            Assert.True(discRes.IsSuccess);
            Assert.Equal("Disconnected", discRes.Value!.State);

            // 6. Terminate
            var termRes = await terminateHandler.HandleAsync(new TerminateCommunicationSessionCommand(connId, "Session closed"));
            Assert.True(termRes.IsSuccess);
            Assert.Equal("Terminated", termRes.Value!.State);
        }

        [Fact]
        public async Task QueryHandlers_RetrieveSessionsCorrectly()
        {
            var repo = new InMemoryCommunicationSessionRepository();
            var establishHandler = new EstablishConnectionCommandHandler(repo);
            var authHandler = new AuthenticateConnectionCommandHandler(repo);

            var connId = "CONN-QUERY-1";
            var pcId = "PC-QUERY-1";

            await establishHandler.HandleAsync(new EstablishConnectionCommand(connId));
            await authHandler.HandleAsync(new AuthenticateConnectionCommand(connId, pcId));

            var getByConnIdHandler = new GetCommunicationSessionByConnectionIdQueryHandler(repo);
            var getByPcIdHandler = new GetCommunicationSessionByPcIdQueryHandler(repo);
            var getActiveHandler = new GetActiveCommunicationSessionsQueryHandler(repo);

            var byConnRes = await getByConnIdHandler.HandleAsync(new GetCommunicationSessionByConnectionIdQuery(connId));
            Assert.True(byConnRes.IsSuccess);
            Assert.NotNull(byConnRes.Value);
            Assert.Equal(connId, byConnRes.Value!.ConnectionId);

            var byPcRes = await getByPcIdHandler.HandleAsync(new GetCommunicationSessionByPcIdQuery(pcId));
            Assert.True(byPcRes.IsSuccess);
            Assert.NotNull(byPcRes.Value);
            Assert.Equal(pcId, byPcRes.Value!.PcId);

            var activeRes = await getActiveHandler.HandleAsync(new GetActiveCommunicationSessionsQuery());
            Assert.True(activeRes.IsSuccess);
            Assert.Single(activeRes.Value!);
        }
    }
}
