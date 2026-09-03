using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Exceptions;
using Sayra.Backend.Shared;

#nullable enable

namespace Sayra.Backend.Application.Configuration
{
    // --- DTOs ---

    public class ConfigurationTargetDto
    {
        public Guid Id { get; set; }
        public string TargetType { get; set; } = string.Empty;
        public Guid OrganizationId { get; set; }
        public Guid? SiteId { get; set; }
        public Guid? GroupId { get; set; }
        public Guid? WorkstationId { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class ConfigurationAssignmentDto
    {
        public Guid Id { get; set; }
        public Guid ConfigurationPackageId { get; set; }
        public Guid ConfigurationTargetId { get; set; }
        public bool IsActive { get; set; }
        public string AssignedBy { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class WorkstationGroupDto
    {
        public Guid Id { get; set; }
        public Guid OrganizationId { get; set; }
        public Guid? SiteId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Status { get; set; } = "Active";
    }

    public class ApplicableAssignmentDto
    {
        public Guid AssignmentId { get; set; }
        public Guid ConfigurationPackageId { get; set; }
        public long VersionNumber { get; set; }
        public string TargetType { get; set; } = string.Empty;
        public Guid TargetId { get; set; }
        public string AssignedBy { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    // --- Commands ---

    public class CreateWorkstationGroupCommand : ICommand<WorkstationGroupDto>
    {
        public Guid OrganizationId { get; set; }
        public Guid? SiteId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
    }

    public class AddWorkstationToGroupCommand : ICommand<bool>
    {
        public Guid GroupId { get; set; }
        public Guid WorkstationId { get; set; }
    }

    public class RemoveWorkstationFromGroupCommand : ICommand<bool>
    {
        public Guid GroupId { get; set; }
        public Guid WorkstationId { get; set; }
    }

    public class CreateConfigurationTargetCommand : ICommand<ConfigurationTargetDto>
    {
        public ConfigurationTargetType TargetType { get; set; }
        public Guid OrganizationId { get; set; }
        public Guid? SiteId { get; set; }
        public Guid? GroupId { get; set; }
        public Guid? WorkstationId { get; set; }
    }

    public class AssignConfigurationToTargetCommand : ICommand<ConfigurationAssignmentDto>
    {
        public Guid ConfigurationPackageId { get; set; }
        public Guid ConfigurationTargetId { get; set; }
        public string AssignedBy { get; set; } = "system";
    }

    public class UnassignConfigurationFromTargetCommand : ICommand<bool>
    {
        public Guid ConfigurationAssignmentId { get; set; }
    }

    // --- Queries ---

    public class GetConfigurationAssignmentsQuery : IQuery<List<ConfigurationAssignmentDto>>
    {
        public Guid? TargetId { get; set; }
        public Guid? PackageId { get; set; }
    }

    public class GetApplicableAssignmentsForWorkstationQuery : IQuery<List<ApplicableAssignmentDto>>
    {
        public Guid WorkstationId { get; set; }
    }

    public class ResolveEffectiveConfigurationQuery : IQuery<Models.ConfigurationResolutionResult>
    {
        public Guid WorkstationId { get; set; }
    }

    // --- Handlers ---

    public class CreateWorkstationGroupCommandHandler : ICommandHandler<CreateWorkstationGroupCommand, WorkstationGroupDto>
    {
        private readonly IWorkstationGroupRepository _groupRepository;
        private readonly IRepository<Organization> _organizationRepository;
        private readonly IRepository<Site> _siteRepository;
        private readonly IUnitOfWork _unitOfWork;

        public CreateWorkstationGroupCommandHandler(
            IWorkstationGroupRepository groupRepository,
            IRepository<Organization> organizationRepository,
            IRepository<Site> siteRepository,
            IUnitOfWork unitOfWork)
        {
            _groupRepository = groupRepository ?? throw new ArgumentNullException(nameof(groupRepository));
            _organizationRepository = organizationRepository ?? throw new ArgumentNullException(nameof(organizationRepository));
            _siteRepository = siteRepository ?? throw new ArgumentNullException(nameof(siteRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        }

        public async Task<Result<WorkstationGroupDto>> HandleAsync(CreateWorkstationGroupCommand command, CancellationToken cancellationToken = default)
        {
            if (command.OrganizationId == Guid.Empty)
            {
                return Result<WorkstationGroupDto>.Failure("INVALID_ORGANIZATION_ID", "OrganizationId is required.");
            }

            var org = await _organizationRepository.GetByIdAsync(command.OrganizationId, track: false, cancellationToken);
            if (org == null)
            {
                return Result<WorkstationGroupDto>.Failure("ORGANIZATION_NOT_FOUND", $"Organization '{command.OrganizationId}' not found.");
            }
            if (!org.CanOperate())
            {
                return Result<WorkstationGroupDto>.Failure("ORGANIZATION_INACTIVE", $"Organization '{org.Name}' is not active.");
            }

            if (command.SiteId.HasValue && command.SiteId.Value != Guid.Empty)
            {
                var site = await _siteRepository.GetByIdAsync(command.SiteId.Value, track: false, cancellationToken);
                if (site == null)
                {
                    return Result<WorkstationGroupDto>.Failure("SITE_NOT_FOUND", $"Site '{command.SiteId}' not found.");
                }
                if (site.OrganizationId != command.OrganizationId)
                {
                    return Result<WorkstationGroupDto>.Failure("CROSS_ORGANIZATION_TARGET_REJECTED", $"Site '{site.Name}' does not belong to Organization '{org.Name}'.");
                }
            }

            var existingGroup = await _groupRepository.GetByCodeAsync(command.OrganizationId, command.Code, cancellationToken);
            if (existingGroup != null)
            {
                return Result<WorkstationGroupDto>.Failure("DUPLICATE_GROUP_CODE", $"Group with code '{command.Code}' already exists for this organization.");
            }

            var group = new WorkstationGroup
            {
                OrganizationId = command.OrganizationId,
                SiteId = command.SiteId,
                Name = command.Name,
                Code = command.Code,
                Status = "Active",
                CreatedAt = DateTime.UtcNow
            };

            group.NormalizeAndValidate();

            await _groupRepository.AddAsync(group, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var dto = new WorkstationGroupDto
            {
                Id = group.Id,
                OrganizationId = group.OrganizationId,
                SiteId = group.SiteId,
                Name = group.Name,
                Code = group.Code,
                Status = group.Status
            };

            return Result<WorkstationGroupDto>.Success(dto);
        }
    }

    public class AddWorkstationToGroupCommandHandler : ICommandHandler<AddWorkstationToGroupCommand, bool>
    {
        private readonly IWorkstationGroupRepository _groupRepository;
        private readonly IRepository<Workstation> _workstationRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IConfigurationCache? _configurationCache;

        public AddWorkstationToGroupCommandHandler(
            IWorkstationGroupRepository groupRepository,
            IRepository<Workstation> workstationRepository,
            IUnitOfWork unitOfWork,
            IConfigurationCache? configurationCache = null)
        {
            _groupRepository = groupRepository ?? throw new ArgumentNullException(nameof(groupRepository));
            _workstationRepository = workstationRepository ?? throw new ArgumentNullException(nameof(workstationRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _configurationCache = configurationCache;
        }

        public async Task<Result<bool>> HandleAsync(AddWorkstationToGroupCommand command, CancellationToken cancellationToken = default)
        {
            var group = await _groupRepository.GetByIdAsync(command.GroupId, track: false, cancellationToken);
            if (group == null)
            {
                return Result<bool>.Failure("GROUP_NOT_FOUND", $"Group '{command.GroupId}' not found.");
            }

            var workstation = await _workstationRepository.GetByIdAsync(command.WorkstationId, track: false, cancellationToken);
            if (workstation == null)
            {
                return Result<bool>.Failure("WORKSTATION_NOT_FOUND", $"Workstation '{command.WorkstationId}' not found.");
            }

            if (workstation.IsDeactivated)
            {
                return Result<bool>.Failure("WORKSTATION_DEACTIVATED", "Cannot add deactivated workstation to group.");
            }

            // Cross-organization validation
            if (workstation.OrganizationEntityId.HasValue && workstation.OrganizationEntityId.Value != group.OrganizationId)
            {
                return Result<bool>.Failure("CROSS_ORGANIZATION_MEMBERSHIP_REJECTED", "Workstation belongs to a different organization than the group.");
            }

            var member = new WorkstationGroupMember
            {
                WorkstationGroupId = command.GroupId,
                WorkstationId = command.WorkstationId,
                JoinedAt = DateTime.UtcNow
            };

            await _groupRepository.AddMemberAsync(member, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (_configurationCache != null)
            {
                await _configurationCache.InvalidateScopeAsync(group.OrganizationId, ConfigurationTargetType.Group, group.Id, cancellationToken);
                await _configurationCache.InvalidateWorkstationAsync(group.OrganizationId, workstation.Id, cancellationToken);
            }

            return Result<bool>.Success(true);
        }
    }

    public class RemoveWorkstationFromGroupCommandHandler : ICommandHandler<RemoveWorkstationFromGroupCommand, bool>
    {
        private readonly IWorkstationGroupRepository _groupRepository;
        private readonly IRepository<Workstation> _workstationRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IConfigurationCache? _configurationCache;

        public RemoveWorkstationFromGroupCommandHandler(
            IWorkstationGroupRepository groupRepository,
            IRepository<Workstation> workstationRepository,
            IUnitOfWork unitOfWork,
            IConfigurationCache? configurationCache = null)
        {
            _groupRepository = groupRepository ?? throw new ArgumentNullException(nameof(groupRepository));
            _workstationRepository = workstationRepository ?? throw new ArgumentNullException(nameof(workstationRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _configurationCache = configurationCache;
        }

        public async Task<Result<bool>> HandleAsync(RemoveWorkstationFromGroupCommand command, CancellationToken cancellationToken = default)
        {
            var group = await _groupRepository.GetByIdAsync(command.GroupId, track: false, cancellationToken);
            var workstation = await _workstationRepository.GetByIdAsync(command.WorkstationId, track: false, cancellationToken);

            await _groupRepository.RemoveMemberAsync(command.GroupId, command.WorkstationId, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (_configurationCache != null && group != null && workstation != null && workstation.OrganizationEntityId.HasValue)
            {
                await _configurationCache.InvalidateScopeAsync(group.OrganizationId, ConfigurationTargetType.Group, group.Id, cancellationToken);
                await _configurationCache.InvalidateWorkstationAsync(workstation.OrganizationEntityId.Value, workstation.Id, cancellationToken);
            }

            return Result<bool>.Success(true);
        }
    }

    public class CreateConfigurationTargetCommandHandler : ICommandHandler<CreateConfigurationTargetCommand, ConfigurationTargetDto>
    {
        private readonly IConfigurationTargetRepository _targetRepository;
        private readonly IRepository<Organization> _organizationRepository;
        private readonly IRepository<Site> _siteRepository;
        private readonly IWorkstationGroupRepository _groupRepository;
        private readonly IRepository<Workstation> _workstationRepository;
        private readonly IUnitOfWork _unitOfWork;

        public CreateConfigurationTargetCommandHandler(
            IConfigurationTargetRepository targetRepository,
            IRepository<Organization> organizationRepository,
            IRepository<Site> siteRepository,
            IWorkstationGroupRepository groupRepository,
            IRepository<Workstation> workstationRepository,
            IUnitOfWork unitOfWork)
        {
            _targetRepository = targetRepository ?? throw new ArgumentNullException(nameof(targetRepository));
            _organizationRepository = organizationRepository ?? throw new ArgumentNullException(nameof(organizationRepository));
            _siteRepository = siteRepository ?? throw new ArgumentNullException(nameof(siteRepository));
            _groupRepository = groupRepository ?? throw new ArgumentNullException(nameof(groupRepository));
            _workstationRepository = workstationRepository ?? throw new ArgumentNullException(nameof(workstationRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        }

        public async Task<Result<ConfigurationTargetDto>> HandleAsync(CreateConfigurationTargetCommand command, CancellationToken cancellationToken = default)
        {
            if (command.OrganizationId == Guid.Empty)
            {
                return Result<ConfigurationTargetDto>.Failure("INVALID_ORGANIZATION_ID", "OrganizationId is required.");
            }

            var org = await _organizationRepository.GetByIdAsync(command.OrganizationId, track: false, cancellationToken);
            if (org == null)
            {
                return Result<ConfigurationTargetDto>.Failure("ORGANIZATION_NOT_FOUND", $"Organization '{command.OrganizationId}' not found.");
            }
            if (!org.CanOperate())
            {
                return Result<ConfigurationTargetDto>.Failure("ORGANIZATION_INACTIVE", $"Organization '{org.Name}' is not active.");
            }

            // Target scope validation & Cross-organization isolation checks
            switch (command.TargetType)
            {
                case ConfigurationTargetType.Global:
                    if (command.SiteId.HasValue || command.GroupId.HasValue || command.WorkstationId.HasValue)
                    {
                        return Result<ConfigurationTargetDto>.Failure("INVALID_TARGET_SCOPES", "Global target cannot specify SiteId, GroupId, or WorkstationId.");
                    }
                    break;

                case ConfigurationTargetType.Site:
                    if (!command.SiteId.HasValue || command.SiteId.Value == Guid.Empty)
                    {
                        return Result<ConfigurationTargetDto>.Failure("INVALID_SITE_ID", "Site target requires SiteId.");
                    }
                    if (command.GroupId.HasValue || command.WorkstationId.HasValue)
                    {
                        return Result<ConfigurationTargetDto>.Failure("INVALID_TARGET_SCOPES", "Site target cannot specify GroupId or WorkstationId.");
                    }
                    var site = await _siteRepository.GetByIdAsync(command.SiteId.Value, track: false, cancellationToken);
                    if (site == null)
                    {
                        return Result<ConfigurationTargetDto>.Failure("SITE_NOT_FOUND", $"Site '{command.SiteId}' not found.");
                    }
                    if (site.OrganizationId != command.OrganizationId)
                    {
                        return Result<ConfigurationTargetDto>.Failure("CROSS_ORGANIZATION_TARGET_REJECTED", $"Site '{site.Name}' belongs to organization '{site.OrganizationId}', not '{command.OrganizationId}'.");
                    }
                    if (!site.CanOperate())
                    {
                        return Result<ConfigurationTargetDto>.Failure("SITE_INACTIVE", $"Site '{site.Name}' is not active.");
                    }
                    break;

                case ConfigurationTargetType.Group:
                    if (!command.GroupId.HasValue || command.GroupId.Value == Guid.Empty)
                    {
                        return Result<ConfigurationTargetDto>.Failure("INVALID_GROUP_ID", "Group target requires GroupId.");
                    }
                    if (command.WorkstationId.HasValue)
                    {
                        return Result<ConfigurationTargetDto>.Failure("INVALID_TARGET_SCOPES", "Group target cannot specify WorkstationId.");
                    }
                    var group = await _groupRepository.GetByIdAsync(command.GroupId.Value, track: false, cancellationToken);
                    if (group == null)
                    {
                        return Result<ConfigurationTargetDto>.Failure("GROUP_NOT_FOUND", $"Group '{command.GroupId}' not found.");
                    }
                    if (group.OrganizationId != command.OrganizationId)
                    {
                        return Result<ConfigurationTargetDto>.Failure("CROSS_ORGANIZATION_TARGET_REJECTED", $"Group '{group.Name}' belongs to organization '{group.OrganizationId}', not '{command.OrganizationId}'.");
                    }
                    if (!group.CanOperate())
                    {
                        return Result<ConfigurationTargetDto>.Failure("GROUP_INACTIVE", $"Group '{group.Name}' is not active.");
                    }
                    break;

                case ConfigurationTargetType.Workstation:
                    if (!command.WorkstationId.HasValue || command.WorkstationId.Value == Guid.Empty)
                    {
                        return Result<ConfigurationTargetDto>.Failure("INVALID_WORKSTATION_ID", "Workstation target requires WorkstationId.");
                    }
                    var workstation = await _workstationRepository.GetByIdAsync(command.WorkstationId.Value, track: false, cancellationToken);
                    if (workstation == null)
                    {
                        return Result<ConfigurationTargetDto>.Failure("WORKSTATION_NOT_FOUND", $"Workstation '{command.WorkstationId}' not found.");
                    }
                    if (workstation.IsDeactivated)
                    {
                        return Result<ConfigurationTargetDto>.Failure("WORKSTATION_DEACTIVATED", "Deactivated workstation cannot be targeted.");
                    }
                    if (workstation.OrganizationEntityId.HasValue && workstation.OrganizationEntityId.Value != command.OrganizationId)
                    {
                        return Result<ConfigurationTargetDto>.Failure("CROSS_ORGANIZATION_TARGET_REJECTED", $"Workstation '{workstation.PcId}' belongs to organization '{workstation.OrganizationEntityId}', not '{command.OrganizationId}'.");
                    }
                    break;

                default:
                    return Result<ConfigurationTargetDto>.Failure("INVALID_TARGET_TYPE", $"Unsupported target type '{command.TargetType}'.");
            }

            // Check if identical target already exists
            var existingTarget = await _targetRepository.GetByScopeAsync(
                command.TargetType,
                command.OrganizationId,
                command.SiteId,
                command.GroupId,
                command.WorkstationId,
                cancellationToken);

            if (existingTarget != null)
            {
                var existingDto = new ConfigurationTargetDto
                {
                    Id = existingTarget.Id,
                    TargetType = existingTarget.TargetType.ToString(),
                    OrganizationId = existingTarget.OrganizationId,
                    SiteId = existingTarget.SiteId,
                    GroupId = existingTarget.GroupId,
                    WorkstationId = existingTarget.WorkstationId,
                    CreatedAt = existingTarget.CreatedAt
                };
                return Result<ConfigurationTargetDto>.Success(existingDto);
            }

            ConfigurationTarget target;
            try
            {
                target = command.TargetType switch
                {
                    ConfigurationTargetType.Global => ConfigurationTarget.CreateGlobal(command.OrganizationId),
                    ConfigurationTargetType.Site => ConfigurationTarget.CreateSite(command.OrganizationId, command.SiteId!.Value),
                    ConfigurationTargetType.Group => ConfigurationTarget.CreateGroup(command.OrganizationId, command.GroupId!.Value, command.SiteId),
                    ConfigurationTargetType.Workstation => ConfigurationTarget.CreateWorkstation(command.OrganizationId, command.WorkstationId!.Value, command.SiteId, command.GroupId),
                    _ => throw new InvalidDomainException("INVALID_TARGET_TYPE", "Invalid target type")
                };
            }
            catch (InvalidDomainException domainEx)
            {
                return Result<ConfigurationTargetDto>.Failure(domainEx.ErrorCode, domainEx.Message);
            }

            await _targetRepository.AddAsync(target, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var resultDto = new ConfigurationTargetDto
            {
                Id = target.Id,
                TargetType = target.TargetType.ToString(),
                OrganizationId = target.OrganizationId,
                SiteId = target.SiteId,
                GroupId = target.GroupId,
                WorkstationId = target.WorkstationId,
                CreatedAt = target.CreatedAt
            };

            return Result<ConfigurationTargetDto>.Success(resultDto);
        }
    }

    public class AssignConfigurationToTargetCommandHandler : ICommandHandler<AssignConfigurationToTargetCommand, ConfigurationAssignmentDto>
    {
        private readonly IConfigurationAssignmentRepository _assignmentRepository;
        private readonly IConfigurationPackageRepository _packageRepository;
        private readonly IConfigurationTargetRepository _targetRepository;
        private readonly ISecurityEventService? _securityEventService;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IConfigurationCache? _configurationCache;

        public AssignConfigurationToTargetCommandHandler(
            IConfigurationAssignmentRepository assignmentRepository,
            IConfigurationPackageRepository packageRepository,
            IConfigurationTargetRepository targetRepository,
            IUnitOfWork unitOfWork,
            IConfigurationCache? configurationCache = null,
            ISecurityEventService? securityEventService = null)
        {
            _assignmentRepository = assignmentRepository ?? throw new ArgumentNullException(nameof(assignmentRepository));
            _packageRepository = packageRepository ?? throw new ArgumentNullException(nameof(packageRepository));
            _targetRepository = targetRepository ?? throw new ArgumentNullException(nameof(targetRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _configurationCache = configurationCache;
            _securityEventService = securityEventService;
        }

        public async Task<Result<ConfigurationAssignmentDto>> HandleAsync(AssignConfigurationToTargetCommand command, CancellationToken cancellationToken = default)
        {
            var package = await _packageRepository.GetByIdAsync(command.ConfigurationPackageId, track: false, cancellationToken);
            if (package == null)
            {
                return Result<ConfigurationAssignmentDto>.Failure("PACKAGE_NOT_FOUND", $"Configuration package '{command.ConfigurationPackageId}' not found.");
            }

            var target = await _targetRepository.GetByIdAsync(command.ConfigurationTargetId, track: false, cancellationToken);
            if (target == null)
            {
                return Result<ConfigurationAssignmentDto>.Failure("TARGET_NOT_FOUND", $"Configuration target '{command.ConfigurationTargetId}' not found.");
            }

            var existingAssignment = await _assignmentRepository.GetAssignmentByPackageAndTargetAsync(
                command.ConfigurationPackageId, command.ConfigurationTargetId, cancellationToken);

            if (existingAssignment != null)
            {
                if (existingAssignment.IsActive)
                {
                    return Result<ConfigurationAssignmentDto>.Failure("DUPLICATE_ASSIGNMENT", "This configuration package version is already actively assigned to this target.");
                }

                // Reactivate
                existingAssignment.Reassign(command.AssignedBy);
                _assignmentRepository.Update(existingAssignment);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                if (_configurationCache != null)
                {
                    var scopeId = ConfigurationCacheInvalidationHelper.GetScopeTargetId(target);
                    await _configurationCache.InvalidateScopeAsync(target.OrganizationId, target.TargetType, scopeId, cancellationToken);
                }

                var reactivatedDto = new ConfigurationAssignmentDto
                {
                    Id = existingAssignment.Id,
                    ConfigurationPackageId = existingAssignment.ConfigurationPackageId,
                    ConfigurationTargetId = existingAssignment.ConfigurationTargetId,
                    IsActive = existingAssignment.IsActive,
                    AssignedBy = existingAssignment.AssignedBy,
                    CreatedAt = existingAssignment.CreatedAt,
                    UpdatedAt = existingAssignment.UpdatedAt
                };

                return Result<ConfigurationAssignmentDto>.Success(reactivatedDto);
            }

            ConfigurationAssignment assignment;
            try
            {
                assignment = ConfigurationAssignment.Create(command.ConfigurationPackageId, command.ConfigurationTargetId, command.AssignedBy);
            }
            catch (InvalidDomainException domainEx)
            {
                return Result<ConfigurationAssignmentDto>.Failure(domainEx.ErrorCode, domainEx.Message);
            }

            await _assignmentRepository.AddAsync(assignment, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (_securityEventService != null)
            {
                await _securityEventService.RecordSecurityEventAsync(
                    eventType: "CONFIG_ASSIGNED",
                    actorId: null,
                    actorType: command.AssignedBy,
                    deviceId: null,
                    organizationId: target.OrganizationId,
                    siteId: target.SiteId,
                    resourceType: "ConfigurationAssignment",
                    resourceId: assignment.Id,
                    action: "ASSIGN",
                    result: "SUCCESS",
                    failureReason: null,
                    cancellationToken: cancellationToken);
            }

            if (_configurationCache != null)
            {
                var scopeId = ConfigurationCacheInvalidationHelper.GetScopeTargetId(target);
                await _configurationCache.InvalidateScopeAsync(target.OrganizationId, target.TargetType, scopeId, cancellationToken);
            }

            var createdDto = new ConfigurationAssignmentDto
            {
                Id = assignment.Id,
                ConfigurationPackageId = assignment.ConfigurationPackageId,
                ConfigurationTargetId = assignment.ConfigurationTargetId,
                IsActive = assignment.IsActive,
                AssignedBy = assignment.AssignedBy,
                CreatedAt = assignment.CreatedAt,
                UpdatedAt = assignment.UpdatedAt
            };

            return Result<ConfigurationAssignmentDto>.Success(createdDto);
        }
    }

    public class UnassignConfigurationFromTargetCommandHandler : ICommandHandler<UnassignConfigurationFromTargetCommand, bool>
    {
        private readonly IConfigurationAssignmentRepository _assignmentRepository;
        private readonly IConfigurationTargetRepository? _targetRepository;
        private readonly ISecurityEventService? _securityEventService;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IConfigurationCache? _configurationCache;

        public UnassignConfigurationFromTargetCommandHandler(
            IConfigurationAssignmentRepository assignmentRepository,
            IUnitOfWork unitOfWork,
            IConfigurationTargetRepository? targetRepository = null,
            IConfigurationCache? configurationCache = null,
            ISecurityEventService? securityEventService = null)
        {
            _assignmentRepository = assignmentRepository ?? throw new ArgumentNullException(nameof(assignmentRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _targetRepository = targetRepository;
            _configurationCache = configurationCache;
            _securityEventService = securityEventService;
        }

        public async Task<Result<bool>> HandleAsync(UnassignConfigurationFromTargetCommand command, CancellationToken cancellationToken = default)
        {
            var assignment = await _assignmentRepository.GetByIdAsync(command.ConfigurationAssignmentId, track: true, cancellationToken);
            if (assignment == null)
            {
                return Result<bool>.Failure("ASSIGNMENT_NOT_FOUND", $"Configuration assignment '{command.ConfigurationAssignmentId}' not found.");
            }

            if (!assignment.IsActive)
            {
                return Result<bool>.Success(true);
            }

            ConfigurationTarget? target = null;
            if (_targetRepository != null)
            {
                target = await _targetRepository.GetByIdAsync(assignment.ConfigurationTargetId, track: false, cancellationToken);
            }

            assignment.Unassign();
            _assignmentRepository.Update(assignment);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (_securityEventService != null)
            {
                await _securityEventService.RecordSecurityEventAsync(
                    eventType: "CONFIG_UNASSIGNED",
                    actorId: null,
                    actorType: "User",
                    deviceId: null,
                    organizationId: target?.OrganizationId,
                    siteId: target?.SiteId,
                    resourceType: "ConfigurationAssignment",
                    resourceId: assignment.Id,
                    action: "UNASSIGN",
                    result: "SUCCESS",
                    failureReason: null,
                    cancellationToken: cancellationToken);
            }

            if (_configurationCache != null && target != null)
            {
                var scopeId = ConfigurationCacheInvalidationHelper.GetScopeTargetId(target);
                await _configurationCache.InvalidateScopeAsync(target.OrganizationId, target.TargetType, scopeId, cancellationToken);
            }

            return Result<bool>.Success(true);
        }
    }

    public class GetConfigurationAssignmentsQueryHandler : IQueryHandler<GetConfigurationAssignmentsQuery, List<ConfigurationAssignmentDto>>
    {
        private readonly IConfigurationAssignmentRepository _assignmentRepository;

        public GetConfigurationAssignmentsQueryHandler(IConfigurationAssignmentRepository assignmentRepository)
        {
            _assignmentRepository = assignmentRepository ?? throw new ArgumentNullException(nameof(assignmentRepository));
        }

        public async Task<Result<List<ConfigurationAssignmentDto>>> HandleAsync(GetConfigurationAssignmentsQuery query, CancellationToken cancellationToken = default)
        {
            List<ConfigurationAssignment> assignments;

            if (query.TargetId.HasValue)
            {
                assignments = await _assignmentRepository.GetAssignmentsForTargetAsync(query.TargetId.Value, cancellationToken);
            }
            else if (query.PackageId.HasValue)
            {
                assignments = await _assignmentRepository.GetAssignmentsForPackageAsync(query.PackageId.Value, cancellationToken);
            }
            else
            {
                var all = await _assignmentRepository.GetAllAsync(track: false, cancellationToken);
                assignments = all.ToList();
            }

            var dtos = assignments.Select(a => new ConfigurationAssignmentDto
            {
                Id = a.Id,
                ConfigurationPackageId = a.ConfigurationPackageId,
                ConfigurationTargetId = a.ConfigurationTargetId,
                IsActive = a.IsActive,
                AssignedBy = a.AssignedBy,
                CreatedAt = a.CreatedAt,
                UpdatedAt = a.UpdatedAt
            }).ToList();

            return Result<List<ConfigurationAssignmentDto>>.Success(dtos);
        }
    }

    public class GetApplicableAssignmentsForWorkstationQueryHandler : IQueryHandler<GetApplicableAssignmentsForWorkstationQuery, List<ApplicableAssignmentDto>>
    {
        private readonly IRepository<Workstation> _workstationRepository;
        private readonly IWorkstationGroupRepository _groupRepository;
        private readonly IConfigurationAssignmentRepository _assignmentRepository;
        private readonly IConfigurationTargetRepository _targetRepository;
        private readonly IConfigurationPackageRepository _packageRepository;

        public GetApplicableAssignmentsForWorkstationQueryHandler(
            IRepository<Workstation> workstationRepository,
            IWorkstationGroupRepository groupRepository,
            IConfigurationAssignmentRepository assignmentRepository,
            IConfigurationTargetRepository targetRepository,
            IConfigurationPackageRepository packageRepository)
        {
            _workstationRepository = workstationRepository ?? throw new ArgumentNullException(nameof(workstationRepository));
            _groupRepository = groupRepository ?? throw new ArgumentNullException(nameof(groupRepository));
            _assignmentRepository = assignmentRepository ?? throw new ArgumentNullException(nameof(assignmentRepository));
            _targetRepository = targetRepository ?? throw new ArgumentNullException(nameof(targetRepository));
            _packageRepository = packageRepository ?? throw new ArgumentNullException(nameof(packageRepository));
        }

        public async Task<Result<List<ApplicableAssignmentDto>>> HandleAsync(GetApplicableAssignmentsForWorkstationQuery query, CancellationToken cancellationToken = default)
        {
            var workstation = await _workstationRepository.GetByIdAsync(query.WorkstationId, track: false, cancellationToken);
            if (workstation == null)
            {
                return Result<List<ApplicableAssignmentDto>>.Failure("WORKSTATION_NOT_FOUND", $"Workstation '{query.WorkstationId}' not found.");
            }

            if (workstation.IsDeactivated)
            {
                return Result<List<ApplicableAssignmentDto>>.Failure("WORKSTATION_DEACTIVATED", "Workstation is deactivated.");
            }

            if (!workstation.OrganizationEntityId.HasValue)
            {
                return Result<List<ApplicableAssignmentDto>>.Failure("WORKSTATION_NOT_ASSIGNED_TO_ORGANIZATION", "Workstation is not assigned to an organization.");
            }

            var orgId = workstation.OrganizationEntityId.Value;
            var siteId = workstation.SiteEntityId;
            var groupIds = await _groupRepository.GetWorkstationGroupIdsForWorkstationAsync(workstation.Id, cancellationToken);

            var activeAssignments = await _assignmentRepository.GetApplicableAssignmentsAsync(
                orgId, siteId, groupIds, workstation.Id, cancellationToken);

            var resultDtos = new List<ApplicableAssignmentDto>();

            foreach (var assignment in activeAssignments)
            {
                var target = await _targetRepository.GetByIdAsync(assignment.ConfigurationTargetId, track: false, cancellationToken);
                var package = await _packageRepository.GetByIdAsync(assignment.ConfigurationPackageId, track: false, cancellationToken);

                if (target != null && package != null)
                {
                    resultDtos.Add(new ApplicableAssignmentDto
                    {
                        AssignmentId = assignment.Id,
                        ConfigurationPackageId = package.Id,
                        VersionNumber = package.VersionNumber,
                        TargetType = target.TargetType.ToString(),
                        TargetId = target.Id,
                        AssignedBy = assignment.AssignedBy,
                        CreatedAt = assignment.CreatedAt
                    });
                }
            }

            return Result<List<ApplicableAssignmentDto>>.Success(resultDtos);
        }
    }

    public class ResolveEffectiveConfigurationQueryHandler : IQueryHandler<ResolveEffectiveConfigurationQuery, Models.ConfigurationResolutionResult>
    {
        private readonly IConfigurationResolver _resolver;

        public ResolveEffectiveConfigurationQueryHandler(IConfigurationResolver resolver)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        }

        public async Task<Result<Models.ConfigurationResolutionResult>> HandleAsync(ResolveEffectiveConfigurationQuery query, CancellationToken cancellationToken = default)
        {
            if (query == null)
            {
                return Result<Models.ConfigurationResolutionResult>.Failure("NULL_QUERY", "Query cannot be null.");
            }

            if (query.WorkstationId == Guid.Empty)
            {
                return Result<Models.ConfigurationResolutionResult>.Failure("INVALID_WORKSTATION_ID", "WorkstationId is required.");
            }

            return await _resolver.ResolveEffectiveConfigurationAsync(query.WorkstationId, cancellationToken);
        }
    }
}
