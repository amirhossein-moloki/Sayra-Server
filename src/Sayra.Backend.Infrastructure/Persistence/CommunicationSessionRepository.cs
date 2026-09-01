using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sayra.Backend.Application.Abstractions.Communication;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Infrastructure.Persistence
{
    public class CommunicationSessionRepository : ICommunicationSessionRepository
    {
        private readonly ApplicationDbContext _context;

        public CommunicationSessionRepository(ApplicationDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public async Task AddAsync(CommunicationSession session, CancellationToken cancellationToken = default)
        {
            await _context.CommunicationSessions.AddAsync(session, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task UpdateAsync(CommunicationSession session, CancellationToken cancellationToken = default)
        {
            _context.CommunicationSessions.Update(session);
            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task<CommunicationSession?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return await _context.CommunicationSessions.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        }

        public async Task<CommunicationSession?> GetByConnectionIdAsync(string connectionId, CancellationToken cancellationToken = default)
        {
            return await _context.CommunicationSessions.FirstOrDefaultAsync(s => s.ConnectionId == connectionId, cancellationToken);
        }

        public async Task<CommunicationSession?> GetByPcIdAsync(string pcId, CancellationToken cancellationToken = default)
        {
            var normalized = pcId.Trim();
            return await _context.CommunicationSessions.FirstOrDefaultAsync(s => s.PcId != null && s.PcId == normalized, cancellationToken);
        }

        public async Task<CommunicationSession?> GetByWorkstationIdAsync(Guid workstationId, CancellationToken cancellationToken = default)
        {
            return await _context.CommunicationSessions.FirstOrDefaultAsync(s => s.WorkstationId == workstationId, cancellationToken);
        }

        public async Task<IReadOnlyList<CommunicationSession>> GetActiveSessionsAsync(CancellationToken cancellationToken = default)
        {
            return await _context.CommunicationSessions
                .Where(s => s.State == ConnectionLifecycleState.Active
                         || s.State == ConnectionLifecycleState.Degraded
                         || s.State == ConnectionLifecycleState.Authenticated)
                .ToListAsync(cancellationToken);
        }
    }
}
