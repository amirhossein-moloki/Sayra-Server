using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Transport;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Exceptions;
using Sayra.Backend.Shared;

namespace Sayra.Backend.Application.Workstations
{
    public class BindWorkstationConnectionCommandHandler : ICommandHandler<BindWorkstationConnectionCommand, Workstation>
    {
        private readonly IRepository<Workstation> _workstationRepository;
        private readonly IRepository<AuditEvent> _auditEventRepository;
        private readonly ITcpConnectionRegistry _connectionRegistry;
        private readonly IUnitOfWork _unitOfWork;

        public BindWorkstationConnectionCommandHandler(
            IRepository<Workstation> workstationRepository,
            IRepository<AuditEvent> auditEventRepository,
            ITcpConnectionRegistry connectionRegistry,
            IUnitOfWork unitOfWork)
        {
            _workstationRepository = workstationRepository;
            _auditEventRepository = auditEventRepository;
            _connectionRegistry = connectionRegistry;
            _unitOfWork = unitOfWork;
        }

        public async Task<Result<Workstation>> HandleAsync(BindWorkstationConnectionCommand command, CancellationToken cancellationToken = default)
        {
            var pcIdUpper = (command.PcId ?? string.Empty).Trim().ToUpperInvariant();

            // 1. Enforce Concurrent Connection Protection (Replace policy)
            var existingConnections = _connectionRegistry.GetAll()
                .Where(c => c.ConnectionId != command.ConnectionId &&
                            c.PcId != null &&
                            c.PcId.Equals(pcIdUpper, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var oldConn in existingConnections)
            {
                // Disconnect old connection gracefully or forcefully
                await oldConn.DisconnectAsync(cancellationToken);
                _connectionRegistry.Unregister(oldConn.ConnectionId);
            }

            // 2. Look up the workstation in DB
            var workstations = await _workstationRepository.GetAllAsync(track: true, cancellationToken);
            var workstation = workstations.FirstOrDefault(w => w.PcId.Equals(pcIdUpper, StringComparison.OrdinalIgnoreCase));

            if (workstation == null)
            {
                throw new DeviceNotRegisteredException($"Workstation {pcIdUpper} is not registered.");
            }

            // 3. Transition Workstation Status to Online
            workstation.TransitionTo("Online");
            workstation.LastSeen = DateTime.UtcNow;

            if (!string.IsNullOrEmpty(command.IpAddress))
            {
                workstation.IpAddress = command.IpAddress;
            }
            if (!string.IsNullOrEmpty(command.ClientVersion))
            {
                workstation.ClientVersion = command.ClientVersion;
            }
            if (!string.IsNullOrEmpty(command.Hostname))
            {
                workstation.Hostname = command.Hostname;
            }
            if (!string.IsNullOrEmpty(command.SiteId))
            {
                workstation.SiteId = command.SiteId;
            }

            _workstationRepository.Update(workstation);

            // 4. Log CLIENT_CONNECTED audit event
            var auditEvent = new AuditEvent
            {
                EventId = Guid.NewGuid(),
                EventType = "CLIENT_CONNECTED",
                EventVersion = 1,
                WorkstationId = workstation.Id,
                Timestamp = DateTime.UtcNow,
                Payload = $"{{\"pcId\":\"{pcIdUpper}\",\"connectionId\":\"{command.ConnectionId}\"}}"
            };
            await _auditEventRepository.AddAsync(auditEvent, cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<Workstation>.Success(workstation);
        }
    }
}
