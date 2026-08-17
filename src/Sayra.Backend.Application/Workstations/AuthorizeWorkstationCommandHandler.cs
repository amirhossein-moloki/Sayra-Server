using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Exceptions;
using Sayra.Backend.Shared;

namespace Sayra.Backend.Application.Workstations
{
    public class AuthorizeWorkstationCommandHandler : ICommandHandler<AuthorizeWorkstationCommand, Workstation>
    {
        private readonly IRepository<Workstation> _workstationRepository;
        private readonly IRepository<AuditEvent> _auditEventRepository;
        private readonly IUnitOfWork _unitOfWork;

        public AuthorizeWorkstationCommandHandler(
            IRepository<Workstation> workstationRepository,
            IRepository<AuditEvent> auditEventRepository,
            IUnitOfWork unitOfWork)
        {
            _workstationRepository = workstationRepository;
            _auditEventRepository = auditEventRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<Result<Workstation>> HandleAsync(AuthorizeWorkstationCommand command, CancellationToken cancellationToken = default)
        {
            var pcIdUpper = (command.PcId ?? string.Empty).Trim().ToUpperInvariant();
            // Performance Optimization: Use database-level indexed query instead of fetching the entire table into memory
            var workstation = await _workstationRepository.FirstOrDefaultAsync(
                w => w.PcId == pcIdUpper,
                track: false,
                cancellationToken);

            if (workstation == null)
            {
                // Log DEVICE_NOT_REGISTERED audit event
                var auditEvent = new AuditEvent
                {
                    EventId = Guid.NewGuid(),
                    EventType = "DEVICE_NOT_REGISTERED",
                    EventVersion = 1,
                    Timestamp = DateTime.UtcNow,
                    Payload = $"{{\"pcId\":\"{pcIdUpper}\"}}"
                };
                await _auditEventRepository.AddAsync(auditEvent, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                throw new DeviceNotRegisteredException($"Workstation {pcIdUpper} is not registered.");
            }

            if (workstation.IsDisabled)
            {
                // Log DEVICE_AUTHORIZATION_FAILED audit event
                var auditEvent = new AuditEvent
                {
                    EventId = Guid.NewGuid(),
                    EventType = "DEVICE_AUTHORIZATION_FAILED",
                    EventVersion = 1,
                    WorkstationId = workstation.Id,
                    Timestamp = DateTime.UtcNow,
                    Payload = $"{{\"pcId\":\"{pcIdUpper}\",\"reason\":\"Device is disabled\"}}"
                };
                await _auditEventRepository.AddAsync(auditEvent, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                throw new AuthFailedException($"Workstation {pcIdUpper} is disabled.");
            }

            return Result<Workstation>.Success(workstation);
        }
    }
}
