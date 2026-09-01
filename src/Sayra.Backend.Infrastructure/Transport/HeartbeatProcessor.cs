using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Sayra.Backend.Application.Abstractions.Communication;
using Sayra.Backend.Domain.ValueObjects;

namespace Sayra.Backend.Infrastructure.Transport
{
    public class HeartbeatProcessor : IHeartbeatProcessor
    {
        private readonly ICommunicationSessionRepository _repository;
        private readonly ILogger<HeartbeatProcessor> _logger;

        public HeartbeatProcessor(
            ICommunicationSessionRepository repository,
            ILogger<HeartbeatProcessor> logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<HeartbeatState> ProcessHeartbeatAsync(string connectionId, DateTime? timestamp = null, CancellationToken cancellationToken = default)
        {
            var session = await _repository.GetByConnectionIdAsync(connectionId, cancellationToken);
            if (session == null)
            {
                return new HeartbeatState(null, DateTime.UtcNow, 1, HeartbeatStatus.TimedOut);
            }

            var now = timestamp ?? DateTime.UtcNow;
            session.RecordHeartbeat(now);
            await _repository.UpdateAsync(session, cancellationToken);

            return session.HeartbeatState;
        }

        public async Task EvaluateSessionLivenessAsync(TimeSpan timeoutThreshold, TimeSpan? degradedThreshold = null, CancellationToken cancellationToken = default)
        {
            var activeSessions = await _repository.GetActiveSessionsAsync(cancellationToken);
            var now = DateTime.UtcNow;

            foreach (var session in activeSessions)
            {
                var newState = session.HeartbeatState.EvaluateLiveness(now, timeoutThreshold, degradedThreshold);

                if (newState.Status == HeartbeatStatus.TimedOut)
                {
                    _logger.LogWarning("Session {ConnectionId} (PcId: {PcId}) timed out due to heartbeat inactivity.", session.ConnectionId, session.PcId);
                    session.Disconnect("Heartbeat Timeout");
                    await _repository.UpdateAsync(session, cancellationToken);
                }
                else if (newState.Status == HeartbeatStatus.Degraded && session.State != Domain.ConnectionLifecycleState.Degraded)
                {
                    _logger.LogInformation("Session {ConnectionId} (PcId: {PcId}) marked degraded due to heartbeat delay.", session.ConnectionId, session.PcId);
                    session.MarkDegraded("Heartbeat Delay");
                    await _repository.UpdateAsync(session, cancellationToken);
                }
            }
        }
    }
}
