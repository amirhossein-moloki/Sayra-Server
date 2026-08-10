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
    public class RegisterWorkstationCommandHandler : ICommandHandler<RegisterWorkstationCommand, Workstation>
    {
        private readonly IRepository<Workstation> _workstationRepository;
        private readonly IRepository<AuditEvent> _auditEventRepository;
        private readonly IUnitOfWork _unitOfWork;

        public RegisterWorkstationCommandHandler(
            IRepository<Workstation> workstationRepository,
            IRepository<AuditEvent> auditEventRepository,
            IUnitOfWork unitOfWork)
        {
            _workstationRepository = workstationRepository;
            _auditEventRepository = auditEventRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<Result<Workstation>> HandleAsync(RegisterWorkstationCommand command, CancellationToken cancellationToken = default)
        {
            try
            {
                // Validate & Normalize using domain entity rules
                var pcIdUpper = (command.PcId ?? string.Empty).Trim().ToUpperInvariant();
                var macNormalized = (command.MacAddress ?? string.Empty).Trim().ToUpperInvariant().Replace("-", ":");

                if (string.IsNullOrWhiteSpace(pcIdUpper))
                {
                    return Result<Workstation>.Failure("INVALID_PC_ID", "PcId is required.");
                }

                // MAC Address Uniqueness Check
                // "MAC address handling must prevent accidental duplicate workstation identities where the business rule requires uniqueness."
                var allWorkstations = await _workstationRepository.GetAllAsync(track: false, cancellationToken);
                var duplicateMacWs = allWorkstations.FirstOrDefault(w => w.MacAddress.Equals(macNormalized, StringComparison.OrdinalIgnoreCase));
                if (duplicateMacWs != null && !duplicateMacWs.PcId.Equals(pcIdUpper, StringComparison.OrdinalIgnoreCase))
                {
                    return Result<Workstation>.Failure("DUPLICATE_MAC_ADDRESS", $"MAC Address {macNormalized} is already registered under another PC: {duplicateMacWs.PcId}");
                }

                var existing = allWorkstations.FirstOrDefault(w => w.PcId.Equals(pcIdUpper, StringComparison.OrdinalIgnoreCase));
                Workstation workstation;

                if (existing != null)
                {
                    // Update existing workstation (idempotency)
                    var tracked = await _workstationRepository.GetByIdAsync(existing.Id, track: true, cancellationToken);
                    if (tracked == null)
                    {
                        return Result<Workstation>.Failure("NOT_FOUND", "Workstation not found.");
                    }

                    tracked.SiteId = command.SiteId ?? string.Empty;
                    tracked.Hostname = command.Hostname ?? string.Empty;
                    tracked.MacAddress = command.MacAddress ?? string.Empty;
                    tracked.IpAddress = command.IpAddress ?? string.Empty;
                    tracked.ClientVersion = command.ClientVersion ?? string.Empty;
                    tracked.OsVersion = command.OsVersion ?? string.Empty;
                    tracked.LastSeen = DateTime.UtcNow;

                    tracked.NormalizeAndValidate();
                    _workstationRepository.Update(tracked);
                    workstation = tracked;
                }
                else
                {
                    // Create new workstation
                    workstation = new Workstation
                    {
                        PcId = command.PcId ?? string.Empty,
                        SiteId = command.SiteId ?? string.Empty,
                        Hostname = command.Hostname ?? string.Empty,
                        MacAddress = command.MacAddress ?? string.Empty,
                        IpAddress = command.IpAddress ?? string.Empty,
                        ClientVersion = command.ClientVersion ?? string.Empty,
                        OsVersion = command.OsVersion ?? string.Empty,
                        Status = "Online", // Initial status on registration
                        LastSeen = DateTime.UtcNow
                    };

                    workstation.NormalizeAndValidate();
                    await _workstationRepository.AddAsync(workstation, cancellationToken);
                }

                // Save audit event
                var auditEvent = new AuditEvent
                {
                    EventId = Guid.NewGuid(),
                    EventType = "CLIENT_REGISTERED",
                    EventVersion = 1,
                    WorkstationId = workstation.Id,
                    Timestamp = DateTime.UtcNow,
                    Payload = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        PcId = workstation.PcId,
                        SiteId = workstation.SiteId,
                        Hostname = workstation.Hostname,
                        MacAddress = workstation.MacAddress,
                        IpAddress = workstation.IpAddress
                    })
                };
                await _auditEventRepository.AddAsync(auditEvent, cancellationToken);

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return Result<Workstation>.Success(workstation);
            }
            catch (InvalidDomainException domainEx)
            {
                return Result<Workstation>.Failure(domainEx.ErrorCode, domainEx.Message);
            }
            catch (Exception ex)
            {
                return Result<Workstation>.Failure("REGISTRATION_FAILED", ex.Message);
            }
        }
    }
}
