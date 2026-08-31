using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Sayra.Backend.Application.Abstractions.Communication;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Infrastructure.Transport
{
    public class CommunicationSessionManager : ICommunicationSessionManager
    {
        private readonly ICommunicationSessionRepository _repository;
        private readonly ILogger<CommunicationSessionManager> _logger;

        public CommunicationSessionManager(
            ICommunicationSessionRepository repository,
            ILogger<CommunicationSessionManager> logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<CommunicationSession> EstablishSessionAsync(string connectionId, string? remoteIpAddress = null, string? hostname = null, CancellationToken cancellationToken = default)
        {
            var existing = await _repository.GetByConnectionIdAsync(connectionId, cancellationToken);
            if (existing != null)
            {
                return existing;
            }

            var session = CommunicationSession.Create(connectionId, remoteIpAddress, hostname);
            await _repository.AddAsync(session, cancellationToken);
            _logger.LogInformation("Communication session established. ConnectionId: {ConnectionId}, SessionId: {SessionId}", connectionId, session.Id);
            return session;
        }

        public async Task<CommunicationSession?> AuthenticateSessionAsync(string connectionId, string pcId, Guid? workstationId = null, CancellationToken cancellationToken = default)
        {
            var session = await _repository.GetByConnectionIdAsync(connectionId, cancellationToken);
            if (session == null)
            {
                _logger.LogWarning("Attempted authentication on unknown connection {ConnectionId}", connectionId);
                return null;
            }

            session.Authenticate(pcId, workstationId);
            await _repository.UpdateAsync(session, cancellationToken);
            _logger.LogInformation("Communication session authenticated. ConnectionId: {ConnectionId}, PcId: {PcId}", connectionId, pcId);
            return session;
        }

        public async Task<CommunicationSession?> ActivateSessionAsync(string connectionId, CancellationToken cancellationToken = default)
        {
            var session = await _repository.GetByConnectionIdAsync(connectionId, cancellationToken);
            if (session == null)
            {
                return null;
            }

            session.Activate();
            await _repository.UpdateAsync(session, cancellationToken);
            _logger.LogInformation("Communication session activated. ConnectionId: {ConnectionId}", connectionId);
            return session;
        }

        public async Task<CommunicationSession?> RecordHeartbeatAsync(string connectionId, DateTime? timestamp = null, CancellationToken cancellationToken = default)
        {
            var session = await _repository.GetByConnectionIdAsync(connectionId, cancellationToken);
            if (session == null)
            {
                return null;
            }

            session.RecordHeartbeat(timestamp);
            await _repository.UpdateAsync(session, cancellationToken);
            return session;
        }

        public async Task<CommunicationSession?> DisconnectSessionAsync(string connectionId, string reason = "Normal Closure", CancellationToken cancellationToken = default)
        {
            var session = await _repository.GetByConnectionIdAsync(connectionId, cancellationToken);
            if (session == null)
            {
                return null;
            }

            session.Disconnect(reason);
            await _repository.UpdateAsync(session, cancellationToken);
            _logger.LogInformation("Communication session disconnected. ConnectionId: {ConnectionId}, Reason: {Reason}", connectionId, reason);
            return session;
        }

        public async Task<CommunicationSession?> TerminateSessionAsync(string connectionId, string reason = "Server Initiated", CancellationToken cancellationToken = default)
        {
            var session = await _repository.GetByConnectionIdAsync(connectionId, cancellationToken);
            if (session == null)
            {
                return null;
            }

            session.Terminate(reason);
            await _repository.UpdateAsync(session, cancellationToken);
            _logger.LogInformation("Communication session terminated. ConnectionId: {ConnectionId}, Reason: {Reason}", connectionId, reason);
            return session;
        }

        public async Task<CommunicationSession?> GetSessionByConnectionIdAsync(string connectionId, CancellationToken cancellationToken = default)
        {
            return await _repository.GetByConnectionIdAsync(connectionId, cancellationToken);
        }

        public async Task<CommunicationSession?> GetSessionByPcIdAsync(string pcId, CancellationToken cancellationToken = default)
        {
            return await _repository.GetByPcIdAsync(pcId, cancellationToken);
        }

        public async Task<IReadOnlyList<CommunicationSession>> GetAllActiveSessionsAsync(CancellationToken cancellationToken = default)
        {
            return await _repository.GetActiveSessionsAsync(cancellationToken);
        }
    }
}
