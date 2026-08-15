using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Events;
using Sayra.Backend.Domain.Exceptions;
using Sayra.Backend.Shared;

namespace Sayra.Backend.Application.Workstations
{
    public class AssignWorkstationCommandHandler : ICommandHandler<AssignWorkstationCommand, WorkstationAssignmentResponseDto>
    {
        private readonly IRepository<Workstation> _workstationRepository;
        private readonly IRepository<Organization> _organizationRepository;
        private readonly IRepository<Site> _siteRepository;
        private readonly IRepository<Zone> _zoneRepository;
        private readonly IRepository<AuditEvent> _auditEventRepository;
        private readonly IUnitOfWork _unitOfWork;

        public AssignWorkstationCommandHandler(
            IRepository<Workstation> workstationRepository,
            IRepository<Organization> organizationRepository,
            IRepository<Site> siteRepository,
            IRepository<Zone> zoneRepository,
            IRepository<AuditEvent> auditEventRepository,
            IUnitOfWork unitOfWork)
        {
            _workstationRepository = workstationRepository ?? throw new ArgumentNullException(nameof(workstationRepository));
            _organizationRepository = organizationRepository ?? throw new ArgumentNullException(nameof(organizationRepository));
            _siteRepository = siteRepository ?? throw new ArgumentNullException(nameof(siteRepository));
            _zoneRepository = zoneRepository ?? throw new ArgumentNullException(nameof(zoneRepository));
            _auditEventRepository = auditEventRepository ?? throw new ArgumentNullException(nameof(auditEventRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        }

        public async Task<Result<WorkstationAssignmentResponseDto>> HandleAsync(AssignWorkstationCommand command, CancellationToken cancellationToken = default)
        {
            try
            {
                var validator = new AssignWorkstationCommandValidator();
                var validationResult = await validator.ValidateAsync(command, cancellationToken);
                if (!validationResult.IsValid)
                {
                    var firstError = validationResult.Errors.First();
                    return Result<WorkstationAssignmentResponseDto>.Failure(firstError.ErrorCode ?? "VALIDATION_FAILED", firstError.ErrorMessage);
                }

                // 1. Validate Workstation
                var workstation = await _workstationRepository.GetByIdAsync(command.WorkstationId, track: true, cancellationToken);
                if (workstation == null)
                {
                    return Result<WorkstationAssignmentResponseDto>.Failure("WORKSTATION_NOT_FOUND", $"Workstation with ID '{command.WorkstationId}' not found.");
                }

                if (workstation.IsDeactivated)
                {
                    return Result<WorkstationAssignmentResponseDto>.Failure("WORKSTATION_DEACTIVATED", "Deactivated workstation cannot be assigned.");
                }

                // 2. Validate Organization
                var org = await _organizationRepository.GetByIdAsync(command.OrganizationId, track: false, cancellationToken);
                if (org == null)
                {
                    return Result<WorkstationAssignmentResponseDto>.Failure("ORGANIZATION_NOT_FOUND", $"Organization with ID '{command.OrganizationId}' not found.");
                }
                if (!org.CanOperate())
                {
                    return Result<WorkstationAssignmentResponseDto>.Failure("ORGANIZATION_INACTIVE", $"Organization '{org.Name}' is not active.");
                }

                // 3. Validate Site ownership & operational status
                var site = await _siteRepository.GetByIdAsync(command.SiteId, track: false, cancellationToken);
                if (site == null)
                {
                    return Result<WorkstationAssignmentResponseDto>.Failure("SITE_NOT_FOUND", $"Site with ID '{command.SiteId}' not found.");
                }
                if (site.OrganizationId != command.OrganizationId)
                {
                    return Result<WorkstationAssignmentResponseDto>.Failure("SITE_ORGANIZATION_MISMATCH", $"Site '{site.Name}' does not belong to Organization '{org.Name}'.");
                }
                if (!site.CanOperate())
                {
                    return Result<WorkstationAssignmentResponseDto>.Failure("SITE_INACTIVE", $"Site '{site.Name}' is not active.");
                }

                // 4. Validate Zone ownership & operational status
                var zone = await _zoneRepository.GetByIdAsync(command.ZoneId, track: false, cancellationToken);
                if (zone == null)
                {
                    return Result<WorkstationAssignmentResponseDto>.Failure("ZONE_NOT_FOUND", $"Zone with ID '{command.ZoneId}' not found.");
                }
                if (zone.SiteId != command.SiteId)
                {
                    return Result<WorkstationAssignmentResponseDto>.Failure("ZONE_SITE_MISMATCH", $"Zone '{zone.Name}' does not belong to Site '{site.Name}'.");
                }
                if (!zone.CanOperate())
                {
                    return Result<WorkstationAssignmentResponseDto>.Failure("ZONE_INACTIVE", $"Zone '{zone.Name}' is disabled or inactive.");
                }

                Guid? previousSiteId = workstation.SiteEntityId;
                Guid? previousZoneId = workstation.ZoneEntityId;
                bool isReassignment = previousSiteId.HasValue || previousZoneId.HasValue;

                // 5. Apply Assignment
                workstation.OrganizationEntityId = command.OrganizationId;
                workstation.SiteEntityId = command.SiteId;
                workstation.ZoneEntityId = command.ZoneId;
                workstation.SiteId = site.Code; // Keep SiteId string synced to Site Code for client compatibility
                workstation.UpdatedAt = DateTime.UtcNow;

                _workstationRepository.Update(workstation);

                // 6. Audit Event
                var auditEvent = new AuditEvent
                {
                    EventId = Guid.NewGuid(),
                    EventType = isReassignment ? nameof(WorkstationAssignmentChanged) : nameof(WorkstationAssigned),
                    EventVersion = 1,
                    WorkstationId = workstation.Id,
                    Timestamp = DateTime.UtcNow,
                    Payload = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        WorkstationId = workstation.Id,
                        PcId = workstation.PcId,
                        OrganizationId = command.OrganizationId,
                        SiteId = command.SiteId,
                        ZoneId = command.ZoneId,
                        PreviousSiteId = previousSiteId,
                        PreviousZoneId = previousZoneId
                    })
                };
                await _auditEventRepository.AddAsync(auditEvent, cancellationToken);

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                var response = new WorkstationAssignmentResponseDto
                {
                    WorkstationId = workstation.Id,
                    PcId = workstation.PcId,
                    OrganizationId = command.OrganizationId,
                    SiteId = command.SiteId,
                    ZoneId = command.ZoneId,
                    SiteCode = site.Code,
                    ZoneCode = zone.Code,
                    AssignedAt = workstation.UpdatedAt ?? DateTime.UtcNow
                };

                return Result<WorkstationAssignmentResponseDto>.Success(response);
            }
            catch (InvalidDomainException domainEx)
            {
                return Result<WorkstationAssignmentResponseDto>.Failure(domainEx.ErrorCode, domainEx.Message);
            }
            catch (Exception ex)
            {
                return Result<WorkstationAssignmentResponseDto>.Failure("ASSIGNMENT_FAILED", ex.Message);
            }
        }
    }

    public class GetWorkstationAssignmentQueryHandler : IQueryHandler<GetWorkstationAssignmentQuery, WorkstationAssignmentResponseDto>
    {
        private readonly IRepository<Workstation> _workstationRepository;
        private readonly IRepository<Site> _siteRepository;
        private readonly IRepository<Zone> _zoneRepository;

        public GetWorkstationAssignmentQueryHandler(
            IRepository<Workstation> workstationRepository,
            IRepository<Site> siteRepository,
            IRepository<Zone> zoneRepository)
        {
            _workstationRepository = workstationRepository ?? throw new ArgumentNullException(nameof(workstationRepository));
            _siteRepository = siteRepository ?? throw new ArgumentNullException(nameof(siteRepository));
            _zoneRepository = zoneRepository ?? throw new ArgumentNullException(nameof(zoneRepository));
        }

        public async Task<Result<WorkstationAssignmentResponseDto>> HandleAsync(GetWorkstationAssignmentQuery query, CancellationToken cancellationToken = default)
        {
            var validator = new GetWorkstationAssignmentQueryValidator();
            var validationResult = await validator.ValidateAsync(query, cancellationToken);
            if (!validationResult.IsValid)
            {
                var firstError = validationResult.Errors.First();
                return Result<WorkstationAssignmentResponseDto>.Failure(firstError.ErrorCode ?? "VALIDATION_FAILED", firstError.ErrorMessage);
            }

            var workstation = await _workstationRepository.GetByIdAsync(query.WorkstationId, track: false, cancellationToken);
            if (workstation == null)
            {
                return Result<WorkstationAssignmentResponseDto>.Failure("NOT_FOUND", $"Workstation with ID '{query.WorkstationId}' not found.");
            }

            if (!workstation.OrganizationEntityId.HasValue || !workstation.SiteEntityId.HasValue || !workstation.ZoneEntityId.HasValue)
            {
                return Result<WorkstationAssignmentResponseDto>.Failure("NOT_ASSIGNED", $"Workstation '{workstation.PcId}' has not been assigned to a business location.");
            }

            var site = await _siteRepository.GetByIdAsync(workstation.SiteEntityId.Value, track: false, cancellationToken);
            var zone = await _zoneRepository.GetByIdAsync(workstation.ZoneEntityId.Value, track: false, cancellationToken);

            var response = new WorkstationAssignmentResponseDto
            {
                WorkstationId = workstation.Id,
                PcId = workstation.PcId,
                OrganizationId = workstation.OrganizationEntityId.Value,
                SiteId = workstation.SiteEntityId.Value,
                ZoneId = workstation.ZoneEntityId.Value,
                SiteCode = site?.Code ?? string.Empty,
                ZoneCode = zone?.Code ?? string.Empty,
                AssignedAt = workstation.UpdatedAt ?? workstation.CreatedAt
            };

            return Result<WorkstationAssignmentResponseDto>.Success(response);
        }
    }
}
