using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Application.Abstractions.Communication
{
    public interface ICommunicationSessionManager
    {
        Task<CommunicationSession> EstablishSessionAsync(string connectionId, string? remoteIpAddress = null, string? hostname = null, CancellationToken cancellationToken = default);
        Task<CommunicationSession?> AuthenticateSessionAsync(string connectionId, string pcId, Guid? workstationId = null, CancellationToken cancellationToken = default);
        Task<CommunicationSession?> ActivateSessionAsync(string connectionId, CancellationToken cancellationToken = default);
        Task<CommunicationSession?> RecordHeartbeatAsync(string connectionId, DateTime? timestamp = null, CancellationToken cancellationToken = default);
        Task<CommunicationSession?> DisconnectSessionAsync(string connectionId, string reason = "Normal Closure", CancellationToken cancellationToken = default);
        Task<CommunicationSession?> TerminateSessionAsync(string connectionId, string reason = "Server Initiated", CancellationToken cancellationToken = default);
        Task<CommunicationSession?> GetSessionByConnectionIdAsync(string connectionId, CancellationToken cancellationToken = default);
        Task<CommunicationSession?> GetSessionByPcIdAsync(string pcId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<CommunicationSession>> GetAllActiveSessionsAsync(CancellationToken cancellationToken = default);
    }
}
