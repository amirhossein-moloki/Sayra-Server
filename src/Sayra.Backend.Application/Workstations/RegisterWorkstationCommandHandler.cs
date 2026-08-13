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
        private readonly IRepository<Site> _siteRepository;
        private readonly IRepository<AuditEvent> _auditEventRepository;
        private readonly IUnitOfWork _unitOfWork;

        public RegisterWorkstationCommandHandler(
            IRepository<Workstation> workstationRepository,
            IRepository<Site> siteRepository,
            IRepository<AuditEvent> auditEventRepository,
            IUnitOfWork unitOfWork)
        {
            _workstationRepository = workstationRepository ?? throw new ArgumentNullException(nameof(workstationRepository));
            _siteRepository = siteRepository ?? throw new ArgumentNullException(nameof(siteRepository));
            _auditEventRepository = auditEventRepository ?? throw new ArgumentNullException(nameof(auditEventRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        }

        public async Task<Result<Workstation>> HandleAsync(RegisterWorkstationCommand command, CancellationToken cancellationToken = default)
        {
            try
            {
                // 1. Fluent Validation
                var validator = new RegisterWorkstationCommandValidator();
                var validationResult = await validator.ValidateAsync(command, cancellationToken);
                if (!validationResult.IsValid)
                {
                    var firstError = validationResult.Errors.First();
                    return Result<Workstation>.Failure(firstError.ErrorCode ?? "VALIDATION_FAILED", firstError.ErrorMessage);
                }

                var pcIdUpper = command.PcId.Trim().ToUpperInvariant();
                var macNormalized = command.MacAddress.Trim().ToUpperInvariant().Replace("-", ":");
                var siteIdNormalized = command.SiteId.Trim().ToUpperInvariant();

                // 2. Validate Site existence
                var allSites = await _siteRepository.GetAllAsync(track: false, cancellationToken);
                var siteExists = allSites.Any(s => s.SiteId.Equals(siteIdNormalized, StringComparison.OrdinalIgnoreCase));
                if (!siteExists)
                {
                    return Result<Workstation>.Failure("INVALID_SITE_ID", $"Site with ID '{command.SiteId}' does not exist.");
                }

                // 3. MAC Address Uniqueness Check
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

                    // Administrative fields are updated, but we preserve PcId (which is immutable) and IsProvisioned/IsDisabled state
                    tracked.SiteId = command.SiteId;
                    tracked.Hostname = command.Hostname;
                    tracked.MacAddress = command.MacAddress;
                    tracked.IpAddress = command.IpAddress;
                    tracked.ClientVersion = command.ClientVersion;
                    tracked.OsVersion = command.OsVersion;
                    tracked.LastSeen = DateTime.UtcNow;

                    tracked.NormalizeAndValidate();
                    _workstationRepository.Update(tracked);
                    workstation = tracked;
                }
                else
                {
                    // Create new workstation - Initial state is OFFLINE
                    workstation = new Workstation
                    {
                        PcId = command.PcId,
                        SiteId = command.SiteId,
                        Hostname = command.Hostname,
                        MacAddress = command.MacAddress,
                        IpAddress = command.IpAddress,
                        ClientVersion = command.ClientVersion,
                        OsVersion = command.OsVersion,
                        Status = "OFFLINE", // Initial state on registration must be OFFLINE
                        LastSeen = DateTime.UtcNow,
                        IsProvisioned = false,
                        IsDisabled = false
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
