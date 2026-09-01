using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Application.Abstractions.Communication
{
    public interface ICommunicationSessionRepository
    {
        Task AddAsync(CommunicationSession session, CancellationToken cancellationToken = default);
        Task UpdateAsync(CommunicationSession session, CancellationToken cancellationToken = default);
        Task<CommunicationSession?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
        Task<CommunicationSession?> GetByConnectionIdAsync(string connectionId, CancellationToken cancellationToken = default);
        Task<CommunicationSession?> GetByPcIdAsync(string pcId, CancellationToken cancellationToken = default);
        Task<CommunicationSession?> GetByWorkstationIdAsync(Guid workstationId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<CommunicationSession>> GetActiveSessionsAsync(CancellationToken cancellationToken = default);
    }
}
