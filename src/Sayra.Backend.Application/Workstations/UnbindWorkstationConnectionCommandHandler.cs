using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Transport;
using Sayra.Backend.Domain;
using Sayra.Backend.Shared;

namespace Sayra.Backend.Application.Workstations
{
    public class UnbindWorkstationConnectionCommandHandler : ICommandHandler<UnbindWorkstationConnectionCommand, Workstation?>
    {
        private readonly IRepository<Workstation> _workstationRepository;
        private readonly IRepository<AuditEvent> _auditEventRepository;
        private readonly ITcpConnectionRegistry? _connectionRegistry;
        private readonly IUnitOfWork _unitOfWork;

        public UnbindWorkstationConnectionCommandHandler(
            IRepository<Workstation> workstationRepository,
            IRepository<AuditEvent> auditEventRepository,
            IUnitOfWork unitOfWork,
            ITcpConnectionRegistry? connectionRegistry = null)
        {
            _workstationRepository = workstationRepository;
            _auditEventRepository = auditEventRepository;
            _unitOfWork = unitOfWork;
            _connectionRegistry = connectionRegistry;
        }

        public async Task<Result<Workstation?>> HandleAsync(UnbindWorkstationConnectionCommand command, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(command.PcId))
            {
                return Result<Workstation?>.Success(null);
            }

            var pcIdUpper = command.PcId.Trim().ToUpperInvariant();
            var workstations = await _workstationRepository.GetAllAsync(track: true, cancellationToken);
            var workstation = workstations.FirstOrDefault(w => w.PcId.Equals(pcIdUpper, StringComparison.OrdinalIgnoreCase));

            if (workstation != null)
            {
                if (_connectionRegistry != null)
                {
                    bool hasOtherActive = _connectionRegistry.GetAll()
                        .Any(c => c.ConnectionId != command.ConnectionId &&
                                  c.PcId != null &&
                                  c.PcId.Equals(pcIdUpper, StringComparison.OrdinalIgnoreCase));
                    if (hasOtherActive)
                    {
                        // Workstation remains Online via another active connection
                        return Result<Workstation?>.Success(workstation);
                    }
                }

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
