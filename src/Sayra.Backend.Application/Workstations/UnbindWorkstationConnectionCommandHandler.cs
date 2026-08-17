using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Domain;
using Sayra.Backend.Shared;

namespace Sayra.Backend.Application.Workstations
{
    public class UnbindWorkstationConnectionCommandHandler : ICommandHandler<UnbindWorkstationConnectionCommand, Workstation?>
    {
        private readonly IRepository<Workstation> _workstationRepository;
        private readonly IRepository<AuditEvent> _auditEventRepository;
        private readonly IUnitOfWork _unitOfWork;

        public UnbindWorkstationConnectionCommandHandler(
            IRepository<Workstation> workstationRepository,
            IRepository<AuditEvent> auditEventRepository,
            IUnitOfWork unitOfWork)
        {
            _workstationRepository = workstationRepository;
            _auditEventRepository = auditEventRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<Result<Workstation?>> HandleAsync(UnbindWorkstationConnectionCommand command, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(command.PcId))
            {
                return Result<Workstation?>.Success(null);
            }

            var pcIdUpper = command.PcId.Trim().ToUpperInvariant();
            // Performance Optimization: Use database-level indexed query instead of fetching the entire table into memory
            var workstation = await _workstationRepository.FirstOrDefaultAsync(
                w => w.PcId == pcIdUpper,
                track: true,
                cancellationToken);

            if (workstation != null)
            {
                // Transition to Offline when connection is lost
                workstation.TransitionTo("Offline");
                workstation.LastSeen = DateTime.UtcNow;

                _workstationRepository.Update(workstation);

                // Log CLIENT_DISCONNECTED audit event
                var auditEvent = new AuditEvent
                {
                    EventId = Guid.NewGuid(),
                    EventType = "CLIENT_DISCONNECTED",
                    EventVersion = 1,
                    WorkstationId = workstation.Id,
                    Timestamp = DateTime.UtcNow,
                    Payload = $"{{\"pcId\":\"{pcIdUpper}\",\"connectionId\":\"{command.ConnectionId}\"}}"
                };
                await _auditEventRepository.AddAsync(auditEvent, cancellationToken);

                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return Result<Workstation?>.Success(workstation);
        }
    }
}
