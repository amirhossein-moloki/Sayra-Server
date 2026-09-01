using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Infrastructure.Persistence
{
    public class RemoteCommandRepository : IRemoteCommandRepository
    {
        private readonly ApplicationDbContext _dbContext;

        public RemoteCommandRepository(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        }

        public async Task<RemoteCommand?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return await _dbContext.RemoteCommands.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        }

        public async Task<RemoteCommand?> GetByCommandIdAsync(string commandId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(commandId)) return null;
            var normalized = commandId.Trim();
            return await _dbContext.RemoteCommands.FirstOrDefaultAsync(c => c.CommandId == normalized, cancellationToken);
        }

        public async Task<IReadOnlyList<RemoteCommand>> GetPendingCommandsByWorkstationAsync(Guid workstationId, CancellationToken cancellationToken = default)
        {
            return await _dbContext.RemoteCommands
                .Where(c => c.TargetWorkstationId == workstationId && (c.Status == "CREATED" || c.Status == "QUEUED" || c.Status == "SENDING"))
                .OrderBy(c => c.CreatedAt)
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<RemoteCommand>> GetActiveCommandsByPcIdAsync(string pcId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(pcId)) return Array.Empty<RemoteCommand>();
            var normalized = pcId.Trim().ToUpperInvariant();
            return await _dbContext.RemoteCommands
                .Where(c => c.TargetPcId == normalized && !RemoteCommand.IsTerminalState(c.Status))
                .OrderBy(c => c.CreatedAt)
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<RemoteCommand>> GetExpiredCommandsAsync(DateTime cutoffTime, CancellationToken cancellationToken = default)
        {
            return await _dbContext.RemoteCommands
                .Where(c => !RemoteCommand.IsTerminalState(c.Status) && c.ExpiresAt != null && c.ExpiresAt <= cutoffTime)
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<RemoteCommand>> GetTimedOutDeliveryCommandsAsync(DateTime cutoffTime, CancellationToken cancellationToken = default)
        {
            return await _dbContext.RemoteCommands
                .Where(c => (c.Status == "SENDING" || c.Status == "QUEUED" || c.Status == "CREATED") && c.CreatedAt <= cutoffTime)
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<RemoteCommand>> GetTimedOutExecutionCommandsAsync(DateTime cutoffTime, CancellationToken cancellationToken = default)
        {
            return await _dbContext.RemoteCommands
                .Where(c => (c.Status == "DELIVERED" || c.Status == "ACKNOWLEDGED" || c.Status == "EXECUTING") && c.DeliveredAt != null && c.DeliveredAt <= cutoffTime)
                .ToListAsync(cancellationToken);
        }

        public async Task AddAsync(RemoteCommand command, CancellationToken cancellationToken = default)
        {
            await _dbContext.RemoteCommands.AddAsync(command, cancellationToken);
        }

        public Task UpdateAsync(RemoteCommand command, CancellationToken cancellationToken = default)
        {
            _dbContext.RemoteCommands.Update(command);
            return Task.CompletedTask;
        }
    }
}
