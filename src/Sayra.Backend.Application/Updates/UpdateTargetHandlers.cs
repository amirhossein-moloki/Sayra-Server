using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Security;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Exceptions;
using Sayra.Backend.Shared;

#nullable enable

namespace Sayra.Backend.Application.Updates
{
    public static class UpdateTargetAdapter
    {
        public static UpdateTargetContract ToContract(UpdateTarget target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            return new UpdateTargetContract
            {
                TargetId = target.Id,
                OrganizationId = target.OrganizationId,
                ReleaseId = target.ReleaseId,
                TargetType = target.TargetType.ToString(),
                SiteId = target.SiteId,
                GroupId = target.GroupId,
                WorkstationId = target.WorkstationId,
                RolloutPercentage = target.RolloutPercentage,
                IsEnabled = target.IsEnabled,
                MinimumSupportedVersion = target.MinimumSupportedVersion,
                IsMandatoryOverride = target.IsMandatoryOverride,
                CreatedBy = target.CreatedBy,
                CreatedAt = target.CreatedAt,
                UpdatedAt = target.UpdatedAt,
                Release = target.Release != null ? ClientUpdateContractAdapter.ToReleaseContract(target.Release) : null
            };
        }
    }

    public class CreateUpdateTargetCommandHandler : ICommandHandler<CreateUpdateTargetCommand, UpdateTargetContract>
    {
        private readonly IUpdateTargetRepository _targetRepository;
        private readonly IUpdateReleaseRepository _releaseRepository;
        private readonly IRepository<Organization> _organizationRepository;
        private readonly IRepository<Site> _siteRepository;
        private readonly IWorkstationGroupRepository _groupRepository;
        private readonly IRepository<Workstation> _workstationRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuthorizationService _authorizationService;
        private readonly ISecurityEventService _securityEventService;

        public CreateUpdateTargetCommandHandler(
            IUpdateTargetRepository targetRepository,
            IUpdateReleaseRepository releaseRepository,
            IRepository<Organization> organizationRepository,
            IRepository<Site> siteRepository,
            IWorkstationGroupRepository groupRepository,
            IRepository<Workstation> workstationRepository,
            IUnitOfWork unitOfWork,
            IAuthorizationService authorizationService,
            ISecurityEventService securityEventService)
        {
            _targetRepository = targetRepository ?? throw new ArgumentNullException(nameof(targetRepository));
            _releaseRepository = releaseRepository ?? throw new ArgumentNullException(nameof(releaseRepository));
            _organizationRepository = organizationRepository ?? throw new ArgumentNullException(nameof(organizationRepository));
            _siteRepository = siteRepository ?? throw new ArgumentNullException(nameof(siteRepository));
            _groupRepository = groupRepository ?? throw new ArgumentNullException(nameof(groupRepository));
            _workstationRepository = workstationRepository ?? throw new ArgumentNullException(nameof(workstationRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
            _securityEventService = securityEventService ?? throw new ArgumentNullException(nameof(securityEventService));
        }

        public async Task<Result<UpdateTargetContract>> HandleAsync(CreateUpdateTargetCommand command, CancellationToken cancellationToken = default)
        {
            if (command == null) return Result<UpdateTargetContract>.Failure("INVALID_COMMAND", "Command cannot be null.");

            var principal = command.Principal ?? UserPrincipal.Anonymous;
            if (!principal.IsAuthenticated)
            {
                return Result<UpdateTargetContract>.Failure("PERMISSION_DENIED", "Authentication is required to create update targets.");
            }

            var authResult = await _authorizationService.AuthorizeAsync(principal, PermissionCatalog.ManageUpdates, null, cancellationToken);
            if (!authResult.IsAllowed)
            {
                authResult = await _authorizationService.AuthorizeAsync(principal, PermissionCatalog.ManageWorkstations, null, cancellationToken);
                if (!authResult.IsAllowed)
                {
                    return Result<UpdateTargetContract>.Failure("PERMISSION_DENIED", "Caller lacks required administrative permissions to manage update targets.");
                }
            }

            var release = await _releaseRepository.GetByIdAsync(command.ReleaseId, track: false, cancellationToken);
            if (release == null)
            {
                return Result<UpdateTargetContract>.Failure("RELEASE_NOT_FOUND", $"Update release '{command.ReleaseId}' was not found.");
            }

            var targetOrgId = release.OrganizationId;
            if (principal.OrganizationId.HasValue && principal.OrganizationId.Value != Guid.Empty && principal.OrganizationId.Value != targetOrgId)
            {
                return Result<UpdateTargetContract>.Failure("CROSS_ORGANIZATION_ACCESS_DENIED", "Access denied: Cannot target releases belonging to a different organization.");
            }

            if (release.Status == UpdateReleaseStatus.Revoked || release.Status == UpdateReleaseStatus.Cancelled)
            {
                return Result<UpdateTargetContract>.Failure("RELEASE_REVOKED", $"Release '{release.Version}' is in '{release.Status}' state and cannot be targeted.");
            }

            // Target scope validation & Cross-organization checks
            switch (command.TargetType)
            {
                case ConfigurationTargetType.Global:
                    if (command.SiteId.HasValue || command.GroupId.HasValue || command.WorkstationId.HasValue)
                    {
                        return Result<UpdateTargetContract>.Failure("INVALID_TARGET_SCOPES", "Global target cannot specify SiteId, GroupId, or WorkstationId.");
                    }
                    break;

                case ConfigurationTargetType.Site:
                    if (!command.SiteId.HasValue || command.SiteId.Value == Guid.Empty)
                    {
                        return Result<UpdateTargetContract>.Failure("INVALID_SITE_ID", "Site target requires SiteId.");
                    }
                    if (command.GroupId.HasValue || command.WorkstationId.HasValue)
                    {
                        return Result<UpdateTargetContract>.Failure("INVALID_TARGET_SCOPES", "Site target cannot specify GroupId or WorkstationId.");
                    }
                    var site = await _siteRepository.GetByIdAsync(command.SiteId.Value, track: false, cancellationToken);
                    if (site == null)
                    {
                        return Result<UpdateTargetContract>.Failure("SITE_NOT_FOUND", $"Site '{command.SiteId}' not found.");
                    }
                    if (site.OrganizationId != targetOrgId)
                    {
                        return Result<UpdateTargetContract>.Failure("CROSS_ORGANIZATION_ACCESS_DENIED", $"Site '{site.Name}' belongs to organization '{site.OrganizationId}', not release organization '{targetOrgId}'.");
                    }
                    break;

                case ConfigurationTargetType.Group:
                    if (!command.GroupId.HasValue || command.GroupId.Value == Guid.Empty)
                    {
                        return Result<UpdateTargetContract>.Failure("INVALID_GROUP_ID", "Group target requires GroupId.");
                    }
                    if (command.WorkstationId.HasValue)
                    {
                        return Result<UpdateTargetContract>.Failure("INVALID_TARGET_SCOPES", "Group target cannot specify WorkstationId.");
                    }
                    var group = await _groupRepository.GetByIdAsync(command.GroupId.Value, track: false, cancellationToken);
                    if (group == null)
                    {
                        return Result<UpdateTargetContract>.Failure("GROUP_NOT_FOUND", $"Group '{command.GroupId}' not found.");
                    }
                    if (group.OrganizationId != targetOrgId)
                    {
                        return Result<UpdateTargetContract>.Failure("CROSS_ORGANIZATION_ACCESS_DENIED", $"Group '{group.Name}' belongs to organization '{group.OrganizationId}', not release organization '{targetOrgId}'.");
                    }
                    break;

                case ConfigurationTargetType.Workstation:
                    if (!command.WorkstationId.HasValue || command.WorkstationId.Value == Guid.Empty)
                    {
                        return Result<UpdateTargetContract>.Failure("INVALID_WORKSTATION_ID", "Workstation target requires WorkstationId.");
                    }
                    var workstation = await _workstationRepository.GetByIdAsync(command.WorkstationId.Value, track: false, cancellationToken);
                    if (workstation == null)
                    {
                        return Result<UpdateTargetContract>.Failure("WORKSTATION_NOT_FOUND", $"Workstation '{command.WorkstationId}' not found.");
                    }
                    if (workstation.IsDeactivated)
                    {
                        return Result<UpdateTargetContract>.Failure("WORKSTATION_DEACTIVATED", "Deactivated workstation cannot be targeted.");
                    }
                    if (workstation.OrganizationEntityId.HasValue && workstation.OrganizationEntityId.Value != targetOrgId)
                    {
                        return Result<UpdateTargetContract>.Failure("CROSS_ORGANIZATION_ACCESS_DENIED", $"Workstation '{workstation.PcId}' belongs to organization '{workstation.OrganizationEntityId}', not release organization '{targetOrgId}'.");
                    }
                    break;
            }

            // Duplicate target scope check
            var existing = await _targetRepository.GetByScopeAsync(
                targetOrgId,
                release.Id,
                command.TargetType,
                command.SiteId,
                command.GroupId,
                command.WorkstationId,
                cancellationToken);

            if (existing != null)
            {
                existing.SetRolloutPercentage(command.RolloutPercentage);
                existing.SetMinimumSupportedVersion(command.MinimumSupportedVersion);
                existing.IsMandatoryOverride = command.IsMandatoryOverride;
                existing.Enable();

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                existing.Release = release;

                return Result<UpdateTargetContract>.Success(UpdateTargetAdapter.ToContract(existing));
            }

            try
            {
                UpdateTarget target = command.TargetType switch
                {
                    ConfigurationTargetType.Global => UpdateTarget.CreateGlobal(
                        targetOrgId, release.Id, command.RolloutPercentage, command.MinimumSupportedVersion, command.IsMandatoryOverride, principal.Username ?? "system"),

                    ConfigurationTargetType.Site => UpdateTarget.CreateSite(
                        targetOrgId, release.Id, command.SiteId!.Value, command.RolloutPercentage, command.MinimumSupportedVersion, command.IsMandatoryOverride, principal.Username ?? "system"),

                    ConfigurationTargetType.Group => UpdateTarget.CreateGroup(
                        targetOrgId, release.Id, command.GroupId!.Value, command.SiteId, command.RolloutPercentage, command.MinimumSupportedVersion, command.IsMandatoryOverride, principal.Username ?? "system"),

                    ConfigurationTargetType.Workstation => UpdateTarget.CreateWorkstation(
                        targetOrgId, release.Id, command.WorkstationId!.Value, command.SiteId, command.GroupId, command.RolloutPercentage, command.MinimumSupportedVersion, command.IsMandatoryOverride, principal.Username ?? "system"),

                    _ => throw new InvalidDomainException("INVALID_TARGET_TYPE", $"Unsupported target type '{command.TargetType}'.")
                };

                await _targetRepository.AddAsync(target, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                target.Release = release;

                await _securityEventService.RecordSecurityEventAsync(
                    eventType: "UPDATE_TARGET_CREATED",
                    actorId: principal.UserId,
                    actorType: "User",
                    deviceId: null,
                    organizationId: targetOrgId,
                    siteId: target.SiteId,
                    resourceType: "UpdateTarget",
                    resourceId: target.Id,
                    action: "CREATE_UPDATE_TARGET",
                    result: "SUCCESS",
                    failureReason: null,
                    cancellationToken: cancellationToken);

                return Result<UpdateTargetContract>.Success(UpdateTargetAdapter.ToContract(target));
            }
            catch (InvalidDomainException domainEx)
            {
                return Result<UpdateTargetContract>.Failure(domainEx.ErrorCode, domainEx.Message);
            }
            catch (Exception ex)
            {
                return Result<UpdateTargetContract>.Failure("CREATE_TARGET_FAILED", $"Failed to create update target: {ex.Message}");
            }
        }
    }

    public class UpdateRolloutPercentageCommandHandler : ICommandHandler<UpdateRolloutPercentageCommand, UpdateTargetContract>
    {
        private readonly IUpdateTargetRepository _targetRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuthorizationService _authorizationService;
        private readonly ISecurityEventService _securityEventService;

        public UpdateRolloutPercentageCommandHandler(
            IUpdateTargetRepository targetRepository,
            IUnitOfWork unitOfWork,
            IAuthorizationService authorizationService,
            ISecurityEventService securityEventService)
        {
            _targetRepository = targetRepository ?? throw new ArgumentNullException(nameof(targetRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
            _securityEventService = securityEventService ?? throw new ArgumentNullException(nameof(securityEventService));
        }

        public async Task<Result<UpdateTargetContract>> HandleAsync(UpdateRolloutPercentageCommand command, CancellationToken cancellationToken = default)
        {
            if (command == null) return Result<UpdateTargetContract>.Failure("INVALID_COMMAND", "Command cannot be null.");

            var principal = command.Principal ?? UserPrincipal.Anonymous;
            if (!principal.IsAuthenticated)
            {
                return Result<UpdateTargetContract>.Failure("PERMISSION_DENIED", "Authentication is required to update rollout percentage.");
            }

            var authResult = await _authorizationService.AuthorizeAsync(principal, PermissionCatalog.ManageUpdates, null, cancellationToken);
            if (!authResult.IsAllowed)
            {
                authResult = await _authorizationService.AuthorizeAsync(principal, PermissionCatalog.ManageWorkstations, null, cancellationToken);
                if (!authResult.IsAllowed)
                {
                    return Result<UpdateTargetContract>.Failure("PERMISSION_DENIED", "Caller lacks required administrative permissions.");
                }
            }

            var target = await _targetRepository.GetByIdAsync(command.TargetId, track: true, cancellationToken);
            if (target == null)
            {
                return Result<UpdateTargetContract>.Failure("TARGET_NOT_FOUND", $"Update target '{command.TargetId}' was not found.");
            }

            if (principal.OrganizationId.HasValue && principal.OrganizationId.Value != Guid.Empty && principal.OrganizationId.Value != target.OrganizationId)
            {
                return Result<UpdateTargetContract>.Failure("CROSS_ORGANIZATION_ACCESS_DENIED", "Access denied: Target belongs to a different organization.");
            }

            try
            {
                int oldPercentage = target.RolloutPercentage;
                target.SetRolloutPercentage(command.RolloutPercentage);

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                string eventType = target.RolloutPercentage switch
                {
                    100 => "UPDATE_ROLLOUT_COMPLETED",
                    > 0 when oldPercentage == 0 => "UPDATE_ROLLOUT_STARTED",
                    _ => "UPDATE_ROLLOUT_CHANGED"
                };

                await _securityEventService.RecordSecurityEventAsync(
                    eventType: eventType,
                    actorId: principal.UserId,
                    actorType: "User",
                    deviceId: null,
                    organizationId: target.OrganizationId,
                    siteId: target.SiteId,
                    resourceType: "UpdateTarget",
                    resourceId: target.Id,
                    action: "UPDATE_ROLLOUT",
                    result: "SUCCESS",
                    failureReason: $"Rollout percentage changed from {oldPercentage}% to {target.RolloutPercentage}%",
                    cancellationToken: cancellationToken);

                return Result<UpdateTargetContract>.Success(UpdateTargetAdapter.ToContract(target));
            }
            catch (InvalidDomainException domainEx)
            {
                return Result<UpdateTargetContract>.Failure(domainEx.ErrorCode, domainEx.Message);
            }
        }
    }

    public class DisableUpdateTargetCommandHandler : ICommandHandler<DisableUpdateTargetCommand, UpdateTargetContract>
    {
        private readonly IUpdateTargetRepository _targetRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuthorizationService _authorizationService;
        private readonly ISecurityEventService _securityEventService;

        public DisableUpdateTargetCommandHandler(
            IUpdateTargetRepository targetRepository,
            IUnitOfWork unitOfWork,
            IAuthorizationService authorizationService,
            ISecurityEventService securityEventService)
        {
            _targetRepository = targetRepository ?? throw new ArgumentNullException(nameof(targetRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
            _securityEventService = securityEventService ?? throw new ArgumentNullException(nameof(securityEventService));
        }

        public async Task<Result<UpdateTargetContract>> HandleAsync(DisableUpdateTargetCommand command, CancellationToken cancellationToken = default)
        {
            var principal = command.Principal ?? UserPrincipal.Anonymous;
            if (!principal.IsAuthenticated) return Result<UpdateTargetContract>.Failure("PERMISSION_DENIED", "Authentication is required.");

            var authResult = await _authorizationService.AuthorizeAsync(principal, PermissionCatalog.ManageUpdates, null, cancellationToken);
            if (!authResult.IsAllowed)
            {
                authResult = await _authorizationService.AuthorizeAsync(principal, PermissionCatalog.ManageWorkstations, null, cancellationToken);
                if (!authResult.IsAllowed) return Result<UpdateTargetContract>.Failure("PERMISSION_DENIED", "Caller lacks required administrative permissions.");
            }

            var target = await _targetRepository.GetByIdAsync(command.TargetId, track: true, cancellationToken);
            if (target == null) return Result<UpdateTargetContract>.Failure("TARGET_NOT_FOUND", $"Update target '{command.TargetId}' was not found.");

            if (principal.OrganizationId.HasValue && principal.OrganizationId.Value != Guid.Empty && principal.OrganizationId.Value != target.OrganizationId)
            {
                return Result<UpdateTargetContract>.Failure("CROSS_ORGANIZATION_ACCESS_DENIED", "Access denied: Target belongs to a different organization.");
            }

            target.Disable();
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await _securityEventService.RecordSecurityEventAsync(
                eventType: "UPDATE_TARGET_DISABLED",
                actorId: principal.UserId,
                actorType: "User",
                deviceId: null,
                organizationId: target.OrganizationId,
                siteId: target.SiteId,
                resourceType: "UpdateTarget",
                resourceId: target.Id,
                action: "DISABLE_TARGET",
                result: "SUCCESS",
                failureReason: null,
                cancellationToken: cancellationToken);

            return Result<UpdateTargetContract>.Success(UpdateTargetAdapter.ToContract(target));
        }
    }

    public class DeleteUpdateTargetCommandHandler : ICommandHandler<DeleteUpdateTargetCommand, bool>
    {
        private readonly IUpdateTargetRepository _targetRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuthorizationService _authorizationService;

        public DeleteUpdateTargetCommandHandler(
            IUpdateTargetRepository targetRepository,
            IUnitOfWork unitOfWork,
            IAuthorizationService authorizationService)
        {
            _targetRepository = targetRepository ?? throw new ArgumentNullException(nameof(targetRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        }

        public async Task<Result<bool>> HandleAsync(DeleteUpdateTargetCommand command, CancellationToken cancellationToken = default)
        {
            var principal = command.Principal ?? UserPrincipal.Anonymous;
            if (!principal.IsAuthenticated) return Result<bool>.Failure("PERMISSION_DENIED", "Authentication required.");

            var authResult = await _authorizationService.AuthorizeAsync(principal, PermissionCatalog.ManageUpdates, null, cancellationToken);
            if (!authResult.IsAllowed) return Result<bool>.Failure("PERMISSION_DENIED", "Caller lacks required administrative permissions.");

            var target = await _targetRepository.GetByIdAsync(command.TargetId, track: true, cancellationToken);
            if (target == null) return Result<bool>.Success(true); // Idempotent

            if (principal.OrganizationId.HasValue && principal.OrganizationId.Value != Guid.Empty && principal.OrganizationId.Value != target.OrganizationId)
            {
                return Result<bool>.Failure("CROSS_ORGANIZATION_ACCESS_DENIED", "Access denied: Target belongs to a different organization.");
            }

            _targetRepository.Delete(target);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<bool>.Success(true);
        }
    }

    public class GetUpdateTargetsByReleaseQueryHandler : IQueryHandler<GetUpdateTargetsByReleaseQuery, IReadOnlyList<UpdateTargetContract>>
    {
        private readonly IUpdateTargetRepository _targetRepository;
        private readonly IUpdateReleaseRepository _releaseRepository;
        private readonly IAuthorizationService _authorizationService;

        public GetUpdateTargetsByReleaseQueryHandler(
            IUpdateTargetRepository targetRepository,
            IUpdateReleaseRepository releaseRepository,
            IAuthorizationService authorizationService)
        {
            _targetRepository = targetRepository ?? throw new ArgumentNullException(nameof(targetRepository));
            _releaseRepository = releaseRepository ?? throw new ArgumentNullException(nameof(releaseRepository));
            _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        }

        public async Task<Result<IReadOnlyList<UpdateTargetContract>>> HandleAsync(GetUpdateTargetsByReleaseQuery query, CancellationToken cancellationToken = default)
        {
            var principal = query.Principal ?? UserPrincipal.Anonymous;
            if (!principal.IsAuthenticated) return Result<IReadOnlyList<UpdateTargetContract>>.Failure("PERMISSION_DENIED", "Authentication required.");

            var authResult = await _authorizationService.AuthorizeAsync(principal, PermissionCatalog.ViewUpdates, null, cancellationToken);
            if (!authResult.IsAllowed) return Result<IReadOnlyList<UpdateTargetContract>>.Failure("PERMISSION_DENIED", "Caller lacks view permissions.");

            var release = await _releaseRepository.GetByIdAsync(query.ReleaseId, track: false, cancellationToken);
            if (release == null) return Result<IReadOnlyList<UpdateTargetContract>>.Failure("RELEASE_NOT_FOUND", $"Release '{query.ReleaseId}' not found.");

            if (principal.OrganizationId.HasValue && principal.OrganizationId.Value != Guid.Empty && principal.OrganizationId.Value != release.OrganizationId)
            {
                return Result<IReadOnlyList<UpdateTargetContract>>.Failure("CROSS_ORGANIZATION_ACCESS_DENIED", "Access denied.");
            }

            var targets = await _targetRepository.GetByReleaseIdAsync(query.ReleaseId, track: false, cancellationToken);
            var contracts = targets.Select(UpdateTargetAdapter.ToContract).ToList();

            return Result<IReadOnlyList<UpdateTargetContract>>.Success(contracts);
        }
    }

    public class GetUpdateTargetsByOrganizationQueryHandler : IQueryHandler<GetUpdateTargetsByOrganizationQuery, IReadOnlyList<UpdateTargetContract>>
    {
        private readonly IUpdateTargetRepository _targetRepository;
        private readonly IAuthorizationService _authorizationService;

        public GetUpdateTargetsByOrganizationQueryHandler(
            IUpdateTargetRepository targetRepository,
            IAuthorizationService authorizationService)
        {
            _targetRepository = targetRepository ?? throw new ArgumentNullException(nameof(targetRepository));
            _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        }

        public async Task<Result<IReadOnlyList<UpdateTargetContract>>> HandleAsync(GetUpdateTargetsByOrganizationQuery query, CancellationToken cancellationToken = default)
        {
            var principal = query.Principal ?? UserPrincipal.Anonymous;
            if (!principal.IsAuthenticated) return Result<IReadOnlyList<UpdateTargetContract>>.Failure("PERMISSION_DENIED", "Authentication required.");

            var authResult = await _authorizationService.AuthorizeAsync(principal, PermissionCatalog.ViewUpdates, null, cancellationToken);
            if (!authResult.IsAllowed) return Result<IReadOnlyList<UpdateTargetContract>>.Failure("PERMISSION_DENIED", "Caller lacks view permissions.");

            var targetOrgId = query.OrganizationId != Guid.Empty ? query.OrganizationId : (principal.OrganizationId ?? Guid.Empty);
            if (targetOrgId == Guid.Empty) return Result<IReadOnlyList<UpdateTargetContract>>.Failure("INVALID_ORGANIZATION", "Organization ID required.");

            if (principal.OrganizationId.HasValue && principal.OrganizationId.Value != Guid.Empty && principal.OrganizationId.Value != targetOrgId)
            {
                return Result<IReadOnlyList<UpdateTargetContract>>.Failure("CROSS_ORGANIZATION_ACCESS_DENIED", "Access denied.");
            }

            var targets = await _targetRepository.GetByOrganizationIdAsync(targetOrgId, track: false, cancellationToken);
            var contracts = targets.Select(UpdateTargetAdapter.ToContract).ToList();

            return Result<IReadOnlyList<UpdateTargetContract>>.Success(contracts);
        }
    }

    public class EvaluateWorkstationUpdateEligibilityQueryHandler : IQueryHandler<EvaluateWorkstationUpdateEligibilityQuery, UpdateEligibilityResult>
    {
        private readonly IUpdateEligibilityService _eligibilityService;
        private readonly IAuthorizationService _authorizationService;
        private readonly ISecurityEventService _securityEventService;

        public EvaluateWorkstationUpdateEligibilityQueryHandler(
            IUpdateEligibilityService eligibilityService,
            IAuthorizationService authorizationService,
            ISecurityEventService securityEventService)
        {
            _eligibilityService = eligibilityService ?? throw new ArgumentNullException(nameof(eligibilityService));
            _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
            _securityEventService = securityEventService ?? throw new ArgumentNullException(nameof(securityEventService));
        }

        public async Task<Result<UpdateEligibilityResult>> HandleAsync(EvaluateWorkstationUpdateEligibilityQuery query, CancellationToken cancellationToken = default)
        {
            if (query == null) return Result<UpdateEligibilityResult>.Failure("INVALID_QUERY", "Query cannot be null.");

            var principal = query.Principal ?? UserPrincipal.Anonymous;
            if (!principal.IsAuthenticated)
            {
                return Result<UpdateEligibilityResult>.Failure("PERMISSION_DENIED", "Authentication required to evaluate update eligibility.");
            }

            var result = await _eligibilityService.EvaluateWorkstationEligibilityAsync(
                query.WorkstationId,
                query.ReportedVersion,
                query.OsVersion,
                query.Architecture,
                cancellationToken);

            string auditEvent = result.IsEligible ? "UPDATE_ELIGIBILITY_EVALUATED" : "UPDATE_ELIGIBILITY_DENIED";

            await _securityEventService.RecordSecurityEventAsync(
                eventType: auditEvent,
                actorId: principal.UserId,
                actorType: "User",
                deviceId: query.WorkstationId.ToString(),
                organizationId: principal.OrganizationId,
                siteId: principal.SiteId,
                resourceType: "Workstation",
                resourceId: query.WorkstationId,
                action: "EVALUATE_ELIGIBILITY",
                result: result.IsEligible ? "SUCCESS" : "DENIED",
                failureReason: result.IsEligible ? null : $"{result.ReasonCode}: {result.ReasonDetails}",
                cancellationToken: cancellationToken);

            return Result<UpdateEligibilityResult>.Success(result);
        }
    }
}
