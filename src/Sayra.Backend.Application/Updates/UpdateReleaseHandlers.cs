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
    public class CreateUpdateReleaseCommandHandler : ICommandHandler<CreateUpdateReleaseCommand, ClientUpdateReleaseContract>
    {
        private readonly IUpdateReleaseRepository _releaseRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuthorizationService _authorizationService;
        private readonly ISecurityEventService _securityEventService;
        private readonly IUpdateMetrics? _updateMetrics;

        public CreateUpdateReleaseCommandHandler(
            IUpdateReleaseRepository releaseRepository,
            IUnitOfWork unitOfWork,
            IAuthorizationService authorizationService,
            ISecurityEventService securityEventService,
            IUpdateMetrics? updateMetrics = null)
        {
            _releaseRepository = releaseRepository ?? throw new ArgumentNullException(nameof(releaseRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
            _securityEventService = securityEventService ?? throw new ArgumentNullException(nameof(securityEventService));
            _updateMetrics = updateMetrics;
        }

        public async Task<Result<ClientUpdateReleaseContract>> HandleAsync(CreateUpdateReleaseCommand command, CancellationToken cancellationToken = default)
        {
            if (command == null)
            {
                return Result<ClientUpdateReleaseContract>.Failure("INVALID_COMMAND", "Command cannot be null.");
            }

            var principal = command.Principal ?? UserPrincipal.Anonymous;
            if (!principal.IsAuthenticated)
            {
                return Result<ClientUpdateReleaseContract>.Failure("PERMISSION_DENIED", "Authentication is required to create update releases.");
            }

            var authResult = await _authorizationService.AuthorizeAsync(principal, PermissionCatalog.ManageUpdates, null, cancellationToken);
            if (!authResult.IsAllowed)
            {
                authResult = await _authorizationService.AuthorizeAsync(principal, PermissionCatalog.ManageWorkstations, null, cancellationToken);
                if (!authResult.IsAllowed)
                {
                    return Result<ClientUpdateReleaseContract>.Failure("PERMISSION_DENIED", "Caller lacks required administrative permissions to create update releases.");
                }
            }

            var targetOrgId = command.OrganizationId != Guid.Empty ? command.OrganizationId : (principal.OrganizationId ?? Guid.Empty);
            if (targetOrgId == Guid.Empty)
            {
                return Result<ClientUpdateReleaseContract>.Failure("INVALID_ORGANIZATION", "Organization ID must be provided or available in principal context.");
            }

            if (principal.OrganizationId.HasValue &&
                principal.OrganizationId.Value != Guid.Empty &&
                principal.OrganizationId.Value != targetOrgId)
            {
                return Result<ClientUpdateReleaseContract>.Failure("CROSS_ORGANIZATION_ACCESS_DENIED", "Access denied: Cannot create release for a different organization.");
            }

            try
            {
                var normalizedVersion = UpdateRelease.NormalizeAndValidateVersion(command.Version);

                var existing = await _releaseRepository.GetByOrganizationAndVersionAsync(targetOrgId, normalizedVersion, false, cancellationToken);
                if (existing != null)
                {
                    return Result<ClientUpdateReleaseContract>.Failure("RELEASE_VERSION_EXISTS", $"An update release with version '{normalizedVersion}' already exists in organization '{targetOrgId}'.");
                }

                var release = UpdateRelease.Create(
                    targetOrgId,
                    normalizedVersion,
                    command.ReleaseType,
                    command.ReleaseNotes,
                    principal.Username ?? principal.UserId?.ToString() ?? "system",
                    command.Metadata);

                await _releaseRepository.AddAsync(release, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                await _securityEventService.RecordSecurityEventAsync(
                    eventType: "UPDATE_RELEASE_CREATED",
                    actorId: principal.UserId,
                    actorType: "User",
                    deviceId: null,
                    organizationId: targetOrgId,
                    siteId: null,
                    resourceType: "UpdateRelease",
                    resourceId: release.Id,
                    action: "CREATE_RELEASE",
                    result: "SUCCESS",
                    failureReason: null,
                    cancellationToken: cancellationToken);

                _updateMetrics?.RecordReleaseOperation("create", "success");

                var contract = ClientUpdateContractAdapter.ToReleaseContract(release);
                return Result<ClientUpdateReleaseContract>.Success(contract);
            }
            catch (InvalidDomainException ex)
            {
                return Result<ClientUpdateReleaseContract>.Failure(ex.ErrorCode, ex.Message);
            }
            catch (Exception ex)
            {
                return Result<ClientUpdateReleaseContract>.Failure("CREATE_RELEASE_FAILED", $"Failed to create update release: {ex.Message}");
            }
        }
    }

    public class UpdateReleaseMetadataCommandHandler : ICommandHandler<UpdateReleaseMetadataCommand, ClientUpdateReleaseContract>
    {
        private readonly IUpdateReleaseRepository _releaseRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuthorizationService _authorizationService;
        private readonly ISecurityEventService _securityEventService;

        public UpdateReleaseMetadataCommandHandler(
            IUpdateReleaseRepository releaseRepository,
            IUnitOfWork unitOfWork,
            IAuthorizationService authorizationService,
            ISecurityEventService securityEventService)
        {
            _releaseRepository = releaseRepository ?? throw new ArgumentNullException(nameof(releaseRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
            _securityEventService = securityEventService ?? throw new ArgumentNullException(nameof(securityEventService));
        }

        public async Task<Result<ClientUpdateReleaseContract>> HandleAsync(UpdateReleaseMetadataCommand command, CancellationToken cancellationToken = default)
        {
            if (command == null)
            {
                return Result<ClientUpdateReleaseContract>.Failure("INVALID_COMMAND", "Command cannot be null.");
            }

            var principal = command.Principal ?? UserPrincipal.Anonymous;
            if (!principal.IsAuthenticated)
            {
                return Result<ClientUpdateReleaseContract>.Failure("PERMISSION_DENIED", "Authentication is required to update release metadata.");
            }

            var authResult = await _authorizationService.AuthorizeAsync(principal, PermissionCatalog.ManageUpdates, null, cancellationToken);
            if (!authResult.IsAllowed)
            {
                authResult = await _authorizationService.AuthorizeAsync(principal, PermissionCatalog.ManageWorkstations, null, cancellationToken);
                if (!authResult.IsAllowed)
                {
                    return Result<ClientUpdateReleaseContract>.Failure("PERMISSION_DENIED", "Caller lacks required administrative permissions.");
                }
            }

            var release = await _releaseRepository.GetByIdAsync(command.ReleaseId, true, cancellationToken);
            if (release == null)
            {
                return Result<ClientUpdateReleaseContract>.Failure("RELEASE_NOT_FOUND", $"Update release '{command.ReleaseId}' was not found.");
            }

            if (principal.OrganizationId.HasValue &&
                principal.OrganizationId.Value != Guid.Empty &&
                principal.OrganizationId.Value != release.OrganizationId)
            {
                return Result<ClientUpdateReleaseContract>.Failure("CROSS_ORGANIZATION_ACCESS_DENIED", "Access denied: Release belongs to a different organization.");
            }

            if (release.IsImmutableState())
            {
                return Result<ClientUpdateReleaseContract>.Failure("RELEASE_IMMUTABLE", $"Release '{release.Version}' in status '{release.Status}' is immutable and metadata cannot be modified.");
            }

            try
            {
                release.UpdateMetadata(command.ReleaseNotes, command.Metadata);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                await _securityEventService.RecordSecurityEventAsync(
                    eventType: "UPDATE_RELEASE_METADATA_UPDATED",
                    actorId: principal.UserId,
                    actorType: "User",
                    deviceId: null,
                    organizationId: release.OrganizationId,
                    siteId: null,
                    resourceType: "UpdateRelease",
                    resourceId: release.Id,
                    action: "UPDATE_METADATA",
                    result: "SUCCESS",
                    failureReason: null,
                    cancellationToken: cancellationToken);

                var contract = ClientUpdateContractAdapter.ToReleaseContract(release);
                return Result<ClientUpdateReleaseContract>.Success(contract);
            }
            catch (InvalidDomainException ex)
            {
                return Result<ClientUpdateReleaseContract>.Failure(ex.ErrorCode, ex.Message);
            }
            catch (Exception ex)
            {
                return Result<ClientUpdateReleaseContract>.Failure("UPDATE_METADATA_FAILED", $"Failed to update release metadata: {ex.Message}");
            }
        }
    }

    public class PrepareUpdateReleaseCommandHandler : ICommandHandler<PrepareUpdateReleaseCommand, ClientUpdateReleaseContract>
    {
        private readonly IUpdateReleaseRepository _releaseRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IUpdateArtifactStorage _storage;
        private readonly IUpdateHashService _hashService;
        private readonly IAuthorizationService _authorizationService;
        private readonly ISecurityEventService _securityEventService;

        public PrepareUpdateReleaseCommandHandler(
            IUpdateReleaseRepository releaseRepository,
            IUnitOfWork unitOfWork,
            IUpdateArtifactStorage storage,
            IUpdateHashService hashService,
            IAuthorizationService authorizationService,
            ISecurityEventService securityEventService)
        {
            _releaseRepository = releaseRepository ?? throw new ArgumentNullException(nameof(releaseRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _hashService = hashService ?? throw new ArgumentNullException(nameof(hashService));
            _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
            _securityEventService = securityEventService ?? throw new ArgumentNullException(nameof(securityEventService));
        }

        public async Task<Result<ClientUpdateReleaseContract>> HandleAsync(PrepareUpdateReleaseCommand command, CancellationToken cancellationToken = default)
        {
            if (command == null)
            {
                return Result<ClientUpdateReleaseContract>.Failure("INVALID_COMMAND", "Command cannot be null.");
            }

            var principal = command.Principal ?? UserPrincipal.Anonymous;
            if (!principal.IsAuthenticated)
            {
                return Result<ClientUpdateReleaseContract>.Failure("PERMISSION_DENIED", "Authentication is required to prepare update release.");
            }

            var authResult = await _authorizationService.AuthorizeAsync(principal, PermissionCatalog.ManageUpdates, null, cancellationToken);
            if (!authResult.IsAllowed)
            {
                authResult = await _authorizationService.AuthorizeAsync(principal, PermissionCatalog.ManageWorkstations, null, cancellationToken);
                if (!authResult.IsAllowed)
                {
                    return Result<ClientUpdateReleaseContract>.Failure("PERMISSION_DENIED", "Caller lacks required administrative permissions.");
                }
            }

            var release = await _releaseRepository.GetByIdAsync(command.ReleaseId, true, cancellationToken);
            if (release == null)
            {
                return Result<ClientUpdateReleaseContract>.Failure("RELEASE_NOT_FOUND", $"Update release '{command.ReleaseId}' was not found.");
            }

            if (principal.OrganizationId.HasValue &&
                principal.OrganizationId.Value != Guid.Empty &&
                principal.OrganizationId.Value != release.OrganizationId)
            {
                return Result<ClientUpdateReleaseContract>.Failure("CROSS_ORGANIZATION_ACCESS_DENIED", "Access denied: Release belongs to a different organization.");
            }

            if (release.Status == UpdateReleaseStatus.Ready)
            {
                // Idempotent
                return Result<ClientUpdateReleaseContract>.Success(ClientUpdateContractAdapter.ToReleaseContract(release));
            }

            var package = release.Packages.FirstOrDefault();
            if (package == null)
            {
                return Result<ClientUpdateReleaseContract>.Failure("RELEASE_NO_PACKAGE", $"Release '{release.Version}' does not have an attached update package.");
            }

            if (package.LifecycleState != UpdatePackageLifecycleState.Signed)
            {
                return Result<ClientUpdateReleaseContract>.Failure("PACKAGE_NOT_SIGNED", $"Package '{package.FileName}' must be signed before release can be prepared. Current state: '{package.LifecycleState}'.");
            }

            if (string.IsNullOrWhiteSpace(package.SHA256) || string.IsNullOrWhiteSpace(package.Signature) || string.IsNullOrWhiteSpace(package.SigningKeyId))
            {
                return Result<ClientUpdateReleaseContract>.Failure("PACKAGE_CRYPTO_METADATA_MISSING", $"Package '{package.FileName}' is missing required cryptographic checksum or signature metadata.");
            }

            // TOCTOU check: verify storage artifact exists and computed SHA-256 matches package authoritative SHA-256
            using var stream = await _storage.OpenReadStreamAsync(package.StorageKey, cancellationToken);
            if (stream == null)
            {
                return Result<ClientUpdateReleaseContract>.Failure("STORAGE_ARTIFACT_NOT_FOUND", $"Artifact for storage key '{package.StorageKey}' was not found in storage.");
            }

            string computedHash = await _hashService.ComputeSha256Async(stream, cancellationToken);
            if (!string.Equals(computedHash, package.SHA256, StringComparison.OrdinalIgnoreCase))
            {
                package.SetVerificationStatus(UpdatePackageVerificationStatus.Quarantined);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                await _securityEventService.RecordSecurityEventAsync(
                    eventType: "UPDATE_RELEASE_PREPARE_FAILED",
                    actorId: principal.UserId,
                    actorType: "User",
                    deviceId: null,
                    organizationId: release.OrganizationId,
                    siteId: null,
                    resourceType: "UpdateRelease",
                    resourceId: release.Id,
                    action: "PREPARE_RELEASE",
                    result: "FAILURE",
                    failureReason: $"TOCTOU integrity violation: computed hash '{computedHash}' does not match stored hash '{package.SHA256}'. Package quarantined.",
                    cancellationToken: cancellationToken);

                return Result<ClientUpdateReleaseContract>.Failure("TOCTOU_INTEGRITY_VIOLATION", "Storage artifact hash does not match package authoritative hash. Package has been quarantined.");
            }

            try
            {
                if (release.Status == UpdateReleaseStatus.Draft)
                {
                    release.TransitionTo(UpdateReleaseStatus.Validated);
                }

                if (release.Status == UpdateReleaseStatus.Validated)
                {
                    release.TransitionTo(UpdateReleaseStatus.Ready);
                }

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                await _securityEventService.RecordSecurityEventAsync(
                    eventType: "UPDATE_RELEASE_READY",
                    actorId: principal.UserId,
                    actorType: "User",
                    deviceId: null,
                    organizationId: release.OrganizationId,
                    siteId: null,
                    resourceType: "UpdateRelease",
                    resourceId: release.Id,
                    action: "PREPARE_RELEASE",
                    result: "SUCCESS",
                    failureReason: null,
                    cancellationToken: cancellationToken);

                return Result<ClientUpdateReleaseContract>.Success(ClientUpdateContractAdapter.ToReleaseContract(release));
            }
            catch (InvalidDomainException ex)
            {
                return Result<ClientUpdateReleaseContract>.Failure(ex.ErrorCode, ex.Message);
            }
            catch (Exception ex)
            {
                return Result<ClientUpdateReleaseContract>.Failure("PREPARE_RELEASE_FAILED", $"Failed to prepare release: {ex.Message}");
            }
        }
    }

    public class PublishUpdateReleaseCommandHandler : ICommandHandler<PublishUpdateReleaseCommand, ClientUpdateReleaseContract>
    {
        private readonly IUpdateReleaseRepository _releaseRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IUpdateArtifactStorage _storage;
        private readonly IUpdateHashService _hashService;
        private readonly IAuthorizationService _authorizationService;
        private readonly ISecurityEventService _securityEventService;
        private readonly IUpdateMetrics? _updateMetrics;

        public PublishUpdateReleaseCommandHandler(
            IUpdateReleaseRepository releaseRepository,
            IUnitOfWork unitOfWork,
            IUpdateArtifactStorage storage,
            IUpdateHashService hashService,
            IAuthorizationService authorizationService,
            ISecurityEventService securityEventService,
            IUpdateMetrics? updateMetrics = null)
        {
            _releaseRepository = releaseRepository ?? throw new ArgumentNullException(nameof(releaseRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _hashService = hashService ?? throw new ArgumentNullException(nameof(hashService));
            _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
            _securityEventService = securityEventService ?? throw new ArgumentNullException(nameof(securityEventService));
            _updateMetrics = updateMetrics;
        }

        public async Task<Result<ClientUpdateReleaseContract>> HandleAsync(PublishUpdateReleaseCommand command, CancellationToken cancellationToken = default)
        {
            if (command == null)
            {
                return Result<ClientUpdateReleaseContract>.Failure("INVALID_COMMAND", "Command cannot be null.");
            }

            var principal = command.Principal ?? UserPrincipal.Anonymous;
            if (!principal.IsAuthenticated)
            {
                return Result<ClientUpdateReleaseContract>.Failure("PERMISSION_DENIED", "Authentication is required to publish update release.");
            }

            var authResult = await _authorizationService.AuthorizeAsync(principal, PermissionCatalog.ManageUpdates, null, cancellationToken);
            if (!authResult.IsAllowed)
            {
                authResult = await _authorizationService.AuthorizeAsync(principal, PermissionCatalog.ManageWorkstations, null, cancellationToken);
                if (!authResult.IsAllowed)
                {
                    return Result<ClientUpdateReleaseContract>.Failure("PERMISSION_DENIED", "Caller lacks required administrative permissions.");
                }
            }

            var release = await _releaseRepository.GetByIdAsync(command.ReleaseId, true, cancellationToken);
            if (release == null)
            {
                return Result<ClientUpdateReleaseContract>.Failure("RELEASE_NOT_FOUND", $"Update release '{command.ReleaseId}' was not found.");
            }

            if (principal.OrganizationId.HasValue &&
                principal.OrganizationId.Value != Guid.Empty &&
                principal.OrganizationId.Value != release.OrganizationId)
            {
                return Result<ClientUpdateReleaseContract>.Failure("CROSS_ORGANIZATION_ACCESS_DENIED", "Access denied: Release belongs to a different organization.");
            }

            if (release.Status == UpdateReleaseStatus.Published)
            {
                // Idempotent
                return Result<ClientUpdateReleaseContract>.Success(ClientUpdateContractAdapter.ToReleaseContract(release));
            }

            if (release.Status != UpdateReleaseStatus.Ready)
            {
                return Result<ClientUpdateReleaseContract>.Failure("INVALID_RELEASE_STATE", $"Release '{release.Version}' must be in 'Ready' status to be published. Current status: '{release.Status}'.");
            }

            var package = release.Packages.FirstOrDefault();
            if (package == null)
            {
                return Result<ClientUpdateReleaseContract>.Failure("RELEASE_NO_PACKAGE", $"Release '{release.Version}' does not have an attached update package.");
            }

            if (package.LifecycleState != UpdatePackageLifecycleState.Signed && package.LifecycleState != UpdatePackageLifecycleState.Ready)
            {
                return Result<ClientUpdateReleaseContract>.Failure("PACKAGE_NOT_SIGNED", $"Package '{package.FileName}' must be signed. Current state: '{package.LifecycleState}'.");
            }

            if (string.IsNullOrWhiteSpace(package.SHA256) || string.IsNullOrWhiteSpace(package.Signature) || string.IsNullOrWhiteSpace(package.SigningKeyId))
            {
                return Result<ClientUpdateReleaseContract>.Failure("PACKAGE_CRYPTO_METADATA_MISSING", $"Package '{package.FileName}' is missing required cryptographic checksum or signature metadata.");
            }

            // TOCTOU check
            using var stream = await _storage.OpenReadStreamAsync(package.StorageKey, cancellationToken);
            if (stream == null)
            {
                return Result<ClientUpdateReleaseContract>.Failure("STORAGE_ARTIFACT_NOT_FOUND", $"Artifact for storage key '{package.StorageKey}' was not found in storage.");
            }

            string computedHash = await _hashService.ComputeSha256Async(stream, cancellationToken);
            if (!string.Equals(computedHash, package.SHA256, StringComparison.OrdinalIgnoreCase))
            {
                package.SetVerificationStatus(UpdatePackageVerificationStatus.Quarantined);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                await _securityEventService.RecordSecurityEventAsync(
                    eventType: "UPDATE_RELEASE_PUBLISH_FAILED",
                    actorId: principal.UserId,
                    actorType: "User",
                    deviceId: null,
                    organizationId: release.OrganizationId,
                    siteId: null,
                    resourceType: "UpdateRelease",
                    resourceId: release.Id,
                    action: "PUBLISH_RELEASE",
                    result: "FAILURE",
                    failureReason: $"TOCTOU integrity violation: computed hash '{computedHash}' does not match stored hash '{package.SHA256}'. Package quarantined.",
                    cancellationToken: cancellationToken);

                return Result<ClientUpdateReleaseContract>.Failure("TOCTOU_INTEGRITY_VIOLATION", "Storage artifact hash does not match package authoritative hash. Package has been quarantined.");
            }

            try
            {
                if (package.LifecycleState == UpdatePackageLifecycleState.Signed)
                {
                    package.TransitionLifecycle(UpdatePackageLifecycleState.Ready);
                }

                release.TransitionTo(UpdateReleaseStatus.Published);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                await _securityEventService.RecordSecurityEventAsync(
                    eventType: "UPDATE_RELEASE_PUBLISHED",
                    actorId: principal.UserId,
                    actorType: "User",
                    deviceId: null,
                    organizationId: release.OrganizationId,
                    siteId: null,
                    resourceType: "UpdateRelease",
                    resourceId: release.Id,
                    action: "PUBLISH_RELEASE",
                    result: "SUCCESS",
                    failureReason: null,
                    cancellationToken: cancellationToken);

                _updateMetrics?.RecordReleaseOperation("publish", "success");

                return Result<ClientUpdateReleaseContract>.Success(ClientUpdateContractAdapter.ToReleaseContract(release));
            }
            catch (InvalidDomainException ex)
            {
                return Result<ClientUpdateReleaseContract>.Failure(ex.ErrorCode, ex.Message);
            }
            catch (Exception ex)
            {
                return Result<ClientUpdateReleaseContract>.Failure("PUBLISH_RELEASE_FAILED", $"Failed to publish release: {ex.Message}");
            }
        }
    }

    public class ActivateUpdateReleaseCommandHandler : ICommandHandler<ActivateUpdateReleaseCommand, ClientUpdateReleaseContract>
    {
        private readonly IUpdateReleaseRepository _releaseRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuthorizationService _authorizationService;
        private readonly ISecurityEventService _securityEventService;

        public ActivateUpdateReleaseCommandHandler(
            IUpdateReleaseRepository releaseRepository,
            IUnitOfWork unitOfWork,
            IAuthorizationService authorizationService,
            ISecurityEventService securityEventService)
        {
            _releaseRepository = releaseRepository ?? throw new ArgumentNullException(nameof(releaseRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
            _securityEventService = securityEventService ?? throw new ArgumentNullException(nameof(securityEventService));
        }

        public async Task<Result<ClientUpdateReleaseContract>> HandleAsync(ActivateUpdateReleaseCommand command, CancellationToken cancellationToken = default)
        {
            if (command == null)
            {
                return Result<ClientUpdateReleaseContract>.Failure("INVALID_COMMAND", "Command cannot be null.");
            }

            var principal = command.Principal ?? UserPrincipal.Anonymous;
            if (!principal.IsAuthenticated)
            {
                return Result<ClientUpdateReleaseContract>.Failure("PERMISSION_DENIED", "Authentication is required to activate update release.");
            }

            var authResult = await _authorizationService.AuthorizeAsync(principal, PermissionCatalog.ManageUpdates, null, cancellationToken);
            if (!authResult.IsAllowed)
            {
                authResult = await _authorizationService.AuthorizeAsync(principal, PermissionCatalog.ManageWorkstations, null, cancellationToken);
                if (!authResult.IsAllowed)
                {
                    return Result<ClientUpdateReleaseContract>.Failure("PERMISSION_DENIED", "Caller lacks required administrative permissions.");
                }
            }

            var targetRelease = await _releaseRepository.GetByIdAsync(command.ReleaseId, true, cancellationToken);
            if (targetRelease == null)
            {
                return Result<ClientUpdateReleaseContract>.Failure("RELEASE_NOT_FOUND", $"Update release '{command.ReleaseId}' was not found.");
            }

            if (principal.OrganizationId.HasValue &&
                principal.OrganizationId.Value != Guid.Empty &&
                principal.OrganizationId.Value != targetRelease.OrganizationId)
            {
                return Result<ClientUpdateReleaseContract>.Failure("CROSS_ORGANIZATION_ACCESS_DENIED", "Access denied: Release belongs to a different organization.");
            }

            if (targetRelease.Status == UpdateReleaseStatus.Active)
            {
                // Idempotent
                return Result<ClientUpdateReleaseContract>.Success(ClientUpdateContractAdapter.ToReleaseContract(targetRelease));
            }

            if (targetRelease.Status != UpdateReleaseStatus.Published)
            {
                return Result<ClientUpdateReleaseContract>.Failure("INVALID_RELEASE_STATE", $"Release '{targetRelease.Version}' must be in 'Published' status to be activated. Current status: '{targetRelease.Status}'.");
            }

            try
            {
                // Find existing active release for the organization
                var activeRelease = await _releaseRepository.GetActiveReleaseAsync(targetRelease.OrganizationId, true, cancellationToken);
                if (activeRelease != null && activeRelease.Id != targetRelease.Id)
                {
                    activeRelease.TransitionTo(UpdateReleaseStatus.Superseded);

                    await _securityEventService.RecordSecurityEventAsync(
                        eventType: "UPDATE_RELEASE_SUPERSEDED",
                        actorId: principal.UserId,
                        actorType: "User",
                        deviceId: null,
                        organizationId: targetRelease.OrganizationId,
                        siteId: null,
                        resourceType: "UpdateRelease",
                        resourceId: activeRelease.Id,
                        action: "SUPERSEDE_RELEASE",
                        result: "SUCCESS",
                        failureReason: $"Superseded by release '{targetRelease.Version}' ({targetRelease.Id})",
                        cancellationToken: cancellationToken);
                }

                targetRelease.TransitionTo(UpdateReleaseStatus.Active);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                await _securityEventService.RecordSecurityEventAsync(
                    eventType: "UPDATE_RELEASE_ACTIVATED",
                    actorId: principal.UserId,
                    actorType: "User",
                    deviceId: null,
                    organizationId: targetRelease.OrganizationId,
                    siteId: null,
                    resourceType: "UpdateRelease",
                    resourceId: targetRelease.Id,
                    action: "ACTIVATE_RELEASE",
                    result: "SUCCESS",
                    failureReason: null,
                    cancellationToken: cancellationToken);

                return Result<ClientUpdateReleaseContract>.Success(ClientUpdateContractAdapter.ToReleaseContract(targetRelease));
            }
            catch (InvalidDomainException ex)
            {
                return Result<ClientUpdateReleaseContract>.Failure(ex.ErrorCode, ex.Message);
            }
            catch (Exception ex)
            {
                return Result<ClientUpdateReleaseContract>.Failure("ACTIVATE_RELEASE_FAILED", $"Failed to activate release: {ex.Message}");
            }
        }
    }

    public class RevokeUpdateReleaseCommandHandler : ICommandHandler<RevokeUpdateReleaseCommand, ClientUpdateReleaseContract>
    {
        private readonly IUpdateReleaseRepository _releaseRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuthorizationService _authorizationService;
        private readonly ISecurityEventService _securityEventService;
        private readonly IUpdateMetrics? _updateMetrics;

        public RevokeUpdateReleaseCommandHandler(
            IUpdateReleaseRepository releaseRepository,
            IUnitOfWork unitOfWork,
            IAuthorizationService authorizationService,
            ISecurityEventService securityEventService,
            IUpdateMetrics? updateMetrics = null)
        {
            _releaseRepository = releaseRepository ?? throw new ArgumentNullException(nameof(releaseRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
            _securityEventService = securityEventService ?? throw new ArgumentNullException(nameof(securityEventService));
            _updateMetrics = updateMetrics;
        }

        public async Task<Result<ClientUpdateReleaseContract>> HandleAsync(RevokeUpdateReleaseCommand command, CancellationToken cancellationToken = default)
        {
            if (command == null)
            {
                return Result<ClientUpdateReleaseContract>.Failure("INVALID_COMMAND", "Command cannot be null.");
            }

            var principal = command.Principal ?? UserPrincipal.Anonymous;
            if (!principal.IsAuthenticated)
            {
                return Result<ClientUpdateReleaseContract>.Failure("PERMISSION_DENIED", "Authentication is required to revoke update release.");
            }

            var authResult = await _authorizationService.AuthorizeAsync(principal, PermissionCatalog.ManageUpdates, null, cancellationToken);
            if (!authResult.IsAllowed)
            {
                authResult = await _authorizationService.AuthorizeAsync(principal, PermissionCatalog.ManageWorkstations, null, cancellationToken);
                if (!authResult.IsAllowed)
                {
                    return Result<ClientUpdateReleaseContract>.Failure("PERMISSION_DENIED", "Caller lacks required administrative permissions.");
                }
            }

            var release = await _releaseRepository.GetByIdAsync(command.ReleaseId, true, cancellationToken);
            if (release == null)
            {
                return Result<ClientUpdateReleaseContract>.Failure("RELEASE_NOT_FOUND", $"Update release '{command.ReleaseId}' was not found.");
            }

            if (principal.OrganizationId.HasValue &&
                principal.OrganizationId.Value != Guid.Empty &&
                principal.OrganizationId.Value != release.OrganizationId)
            {
                return Result<ClientUpdateReleaseContract>.Failure("CROSS_ORGANIZATION_ACCESS_DENIED", "Access denied: Release belongs to a different organization.");
            }

            if (release.Status == UpdateReleaseStatus.Revoked)
            {
                // Idempotent
                return Result<ClientUpdateReleaseContract>.Success(ClientUpdateContractAdapter.ToReleaseContract(release));
            }

            if (release.Status != UpdateReleaseStatus.Published &&
                release.Status != UpdateReleaseStatus.Active &&
                release.Status != UpdateReleaseStatus.Superseded)
            {
                return Result<ClientUpdateReleaseContract>.Failure("INVALID_RELEASE_STATE", $"Release '{release.Version}' in status '{release.Status}' cannot be revoked. Only Published, Active, or Superseded releases can be revoked.");
            }

            try
            {
                release.TransitionTo(UpdateReleaseStatus.Revoked);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                await _securityEventService.RecordSecurityEventAsync(
                    eventType: "UPDATE_RELEASE_REVOKED",
                    actorId: principal.UserId,
                    actorType: "User",
                    deviceId: null,
                    organizationId: release.OrganizationId,
                    siteId: null,
                    resourceType: "UpdateRelease",
                    resourceId: release.Id,
                    action: "REVOKE_RELEASE",
                    result: "SUCCESS",
                    failureReason: string.IsNullOrWhiteSpace(command.Reason) ? "Emergency Administrative Revocation" : command.Reason.Trim(),
                    cancellationToken: cancellationToken);

                _updateMetrics?.RecordReleaseOperation("revoke", "success");

                return Result<ClientUpdateReleaseContract>.Success(ClientUpdateContractAdapter.ToReleaseContract(release));
            }
            catch (InvalidDomainException ex)
            {
                return Result<ClientUpdateReleaseContract>.Failure(ex.ErrorCode, ex.Message);
            }
            catch (Exception ex)
            {
                return Result<ClientUpdateReleaseContract>.Failure("REVOKE_RELEASE_FAILED", $"Failed to revoke release: {ex.Message}");
            }
        }
    }

    public class RollbackUpdateReleaseCommandHandler : ICommandHandler<RollbackUpdateReleaseCommand, ClientUpdateReleaseContract>
    {
        private readonly IUpdateReleaseRepository _releaseRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IUpdateArtifactStorage _storage;
        private readonly IUpdateHashService _hashService;
        private readonly IAuthorizationService _authorizationService;
        private readonly ISecurityEventService _securityEventService;
        private readonly IUpdateMetrics? _updateMetrics;

        public RollbackUpdateReleaseCommandHandler(
            IUpdateReleaseRepository releaseRepository,
            IUnitOfWork unitOfWork,
            IUpdateArtifactStorage storage,
            IUpdateHashService hashService,
            IAuthorizationService authorizationService,
            ISecurityEventService securityEventService,
            IUpdateMetrics? updateMetrics = null)
        {
            _releaseRepository = releaseRepository ?? throw new ArgumentNullException(nameof(releaseRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _hashService = hashService ?? throw new ArgumentNullException(nameof(hashService));
            _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
            _securityEventService = securityEventService ?? throw new ArgumentNullException(nameof(securityEventService));
            _updateMetrics = updateMetrics;
        }

        public async Task<Result<ClientUpdateReleaseContract>> HandleAsync(RollbackUpdateReleaseCommand command, CancellationToken cancellationToken = default)
        {
            if (command == null)
            {
                return Result<ClientUpdateReleaseContract>.Failure("INVALID_COMMAND", "Command cannot be null.");
            }

            var principal = command.Principal ?? UserPrincipal.Anonymous;
            if (!principal.IsAuthenticated)
            {
                return Result<ClientUpdateReleaseContract>.Failure("PERMISSION_DENIED", "Authentication is required to execute release rollback.");
            }

            var authResult = await _authorizationService.AuthorizeAsync(principal, PermissionCatalog.ManageUpdates, null, cancellationToken);
            if (!authResult.IsAllowed)
            {
                authResult = await _authorizationService.AuthorizeAsync(principal, PermissionCatalog.ManageWorkstations, null, cancellationToken);
                if (!authResult.IsAllowed)
                {
                    return Result<ClientUpdateReleaseContract>.Failure("PERMISSION_DENIED", "Caller lacks required administrative permissions.");
                }
            }

            var currentRelease = await _releaseRepository.GetByIdAsync(command.CurrentReleaseId, true, cancellationToken);
            if (currentRelease == null)
            {
                return Result<ClientUpdateReleaseContract>.Failure("RELEASE_NOT_FOUND", $"Current release '{command.CurrentReleaseId}' was not found.");
            }

            var targetRelease = await _releaseRepository.GetByIdAsync(command.TargetReleaseId, true, cancellationToken);
            if (targetRelease == null)
            {
                return Result<ClientUpdateReleaseContract>.Failure("TARGET_RELEASE_NOT_FOUND", $"Target rollback release '{command.TargetReleaseId}' was not found.");
            }

            if (currentRelease.OrganizationId != targetRelease.OrganizationId)
            {
                return Result<ClientUpdateReleaseContract>.Failure("CROSS_ORGANIZATION_ACCESS_DENIED", "Cannot rollback across different organization boundaries.");
            }

            if (principal.OrganizationId.HasValue &&
                principal.OrganizationId.Value != Guid.Empty &&
                principal.OrganizationId.Value != currentRelease.OrganizationId)
            {
                return Result<ClientUpdateReleaseContract>.Failure("CROSS_ORGANIZATION_ACCESS_DENIED", "Access denied: Release belongs to a different organization.");
            }

            if (targetRelease.Status == UpdateReleaseStatus.Revoked || targetRelease.Status == UpdateReleaseStatus.Cancelled)
            {
                return Result<ClientUpdateReleaseContract>.Failure("TARGET_RELEASE_REVOKED", $"Target release '{targetRelease.Version}' is in '{targetRelease.Status}' state and cannot be used as a rollback target.");
            }

            var targetPackage = targetRelease.Packages.FirstOrDefault();
            if (targetPackage == null)
            {
                return Result<ClientUpdateReleaseContract>.Failure("TARGET_RELEASE_NO_PACKAGE", $"Target release '{targetRelease.Version}' has no attached package.");
            }

            if (string.IsNullOrWhiteSpace(targetPackage.SHA256) || string.IsNullOrWhiteSpace(targetPackage.Signature))
            {
                return Result<ClientUpdateReleaseContract>.Failure("TARGET_PACKAGE_INVALID", $"Target package '{targetPackage.FileName}' is missing cryptographic hash or signature metadata.");
            }

            // Verify target artifact integrity in storage
            using var stream = await _storage.OpenReadStreamAsync(targetPackage.StorageKey, cancellationToken);
            if (stream == null)
            {
                return Result<ClientUpdateReleaseContract>.Failure("STORAGE_ARTIFACT_NOT_FOUND", $"Artifact for storage key '{targetPackage.StorageKey}' was not found in storage.");
            }

            string computedHash = await _hashService.ComputeSha256Async(stream, cancellationToken);
            if (!string.Equals(computedHash, targetPackage.SHA256, StringComparison.OrdinalIgnoreCase))
            {
                targetPackage.SetVerificationStatus(UpdatePackageVerificationStatus.Quarantined);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                await _securityEventService.RecordSecurityEventAsync(
                    eventType: "UPDATE_RELEASE_ROLLBACK_FAILED",
                    actorId: principal.UserId,
                    actorType: "User",
                    deviceId: null,
                    organizationId: currentRelease.OrganizationId,
                    siteId: null,
                    resourceType: "UpdateRelease",
                    resourceId: targetRelease.Id,
                    action: "ROLLBACK_RELEASE",
                    result: "FAILURE",
                    failureReason: $"TOCTOU integrity violation on target package: computed hash '{computedHash}' does not match stored hash '{targetPackage.SHA256}'. Package quarantined.",
                    cancellationToken: cancellationToken);

                return Result<ClientUpdateReleaseContract>.Failure("TOCTOU_INTEGRITY_VIOLATION", "Target release artifact hash does not match stored hash. Target package quarantined.");
            }

            try
            {
                await _securityEventService.RecordSecurityEventAsync(
                    eventType: "UPDATE_RELEASE_ROLLBACK_STARTED",
                    actorId: principal.UserId,
                    actorType: "User",
                    deviceId: null,
                    organizationId: currentRelease.OrganizationId,
                    siteId: null,
                    resourceType: "UpdateRelease",
                    resourceId: currentRelease.Id,
                    action: "ROLLBACK_RELEASE",
                    result: "STARTED",
                    failureReason: $"Rolling back from '{currentRelease.Version}' ({currentRelease.Id}) to '{targetRelease.Version}' ({targetRelease.Id}). Reason: {command.Reason}",
                    cancellationToken: cancellationToken);

                // Revoke current problematic release if it was Active or Published
                if (currentRelease.Status == UpdateReleaseStatus.Active || currentRelease.Status == UpdateReleaseStatus.Published)
                {
                    currentRelease.TransitionTo(UpdateReleaseStatus.Revoked);
                }

                // Activate or re-publish target release
                if (targetRelease.Status == UpdateReleaseStatus.Published || targetRelease.Status == UpdateReleaseStatus.Superseded)
                {
                    targetRelease.TransitionTo(UpdateReleaseStatus.Active);
                }
                else if (targetRelease.Status == UpdateReleaseStatus.Ready)
                {
                    targetRelease.TransitionTo(UpdateReleaseStatus.Published);
                    targetRelease.TransitionTo(UpdateReleaseStatus.Active);
                }

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                await _securityEventService.RecordSecurityEventAsync(
                    eventType: "UPDATE_RELEASE_ROLLBACK_COMPLETED",
                    actorId: principal.UserId,
                    actorType: "User",
                    deviceId: null,
                    organizationId: currentRelease.OrganizationId,
                    siteId: null,
                    resourceType: "UpdateRelease",
                    resourceId: targetRelease.Id,
                    action: "ROLLBACK_RELEASE",
                    result: "SUCCESS",
                    failureReason: null,
                    cancellationToken: cancellationToken);

                _updateMetrics?.RecordReleaseOperation("rollback", "success");

                return Result<ClientUpdateReleaseContract>.Success(ClientUpdateContractAdapter.ToReleaseContract(targetRelease));
            }
            catch (InvalidDomainException ex)
            {
                await _securityEventService.RecordSecurityEventAsync(
                    eventType: "UPDATE_RELEASE_ROLLBACK_FAILED",
                    actorId: principal.UserId,
                    actorType: "User",
                    deviceId: null,
                    organizationId: currentRelease.OrganizationId,
                    siteId: null,
                    resourceType: "UpdateRelease",
                    resourceId: targetRelease.Id,
                    action: "ROLLBACK_RELEASE",
                    result: "FAILURE",
                    failureReason: ex.Message,
                    cancellationToken: cancellationToken);

                return Result<ClientUpdateReleaseContract>.Failure(ex.ErrorCode, ex.Message);
            }
            catch (Exception ex)
            {
                await _securityEventService.RecordSecurityEventAsync(
                    eventType: "UPDATE_RELEASE_ROLLBACK_FAILED",
                    actorId: principal.UserId,
                    actorType: "User",
                    deviceId: null,
                    organizationId: currentRelease.OrganizationId,
                    siteId: null,
                    resourceType: "UpdateRelease",
                    resourceId: targetRelease.Id,
                    action: "ROLLBACK_RELEASE",
                    result: "FAILURE",
                    failureReason: ex.Message,
                    cancellationToken: cancellationToken);

                return Result<ClientUpdateReleaseContract>.Failure("ROLLBACK_FAILED", $"Failed to execute release rollback: {ex.Message}");
            }
        }
    }

    public class GetUpdateReleaseByIdQueryHandler : IQueryHandler<GetUpdateReleaseByIdQuery, ClientUpdateReleaseContract>
    {
        private readonly IUpdateReleaseRepository _releaseRepository;
        private readonly IAuthorizationService _authorizationService;

        public GetUpdateReleaseByIdQueryHandler(
            IUpdateReleaseRepository releaseRepository,
            IAuthorizationService authorizationService)
        {
            _releaseRepository = releaseRepository ?? throw new ArgumentNullException(nameof(releaseRepository));
            _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        }

        public async Task<Result<ClientUpdateReleaseContract>> HandleAsync(GetUpdateReleaseByIdQuery query, CancellationToken cancellationToken = default)
        {
            if (query == null)
            {
                return Result<ClientUpdateReleaseContract>.Failure("INVALID_QUERY", "Query cannot be null.");
            }

            var principal = query.Principal ?? UserPrincipal.Anonymous;
            if (!principal.IsAuthenticated)
            {
                return Result<ClientUpdateReleaseContract>.Failure("PERMISSION_DENIED", "Authentication is required to query update releases.");
            }

            var authResult = await _authorizationService.AuthorizeAsync(principal, PermissionCatalog.ViewUpdates, null, cancellationToken);
            if (!authResult.IsAllowed)
            {
                authResult = await _authorizationService.AuthorizeAsync(principal, PermissionCatalog.ViewWorkstations, null, cancellationToken);
                if (!authResult.IsAllowed)
                {
                    return Result<ClientUpdateReleaseContract>.Failure("PERMISSION_DENIED", "Caller lacks required permissions.");
                }
            }

            var release = await _releaseRepository.GetByIdAsync(query.ReleaseId, false, cancellationToken);
            if (release == null)
            {
                return Result<ClientUpdateReleaseContract>.Failure("RELEASE_NOT_FOUND", $"Update release '{query.ReleaseId}' was not found.");
            }

            if (principal.OrganizationId.HasValue &&
                principal.OrganizationId.Value != Guid.Empty &&
                principal.OrganizationId.Value != release.OrganizationId)
            {
                return Result<ClientUpdateReleaseContract>.Failure("CROSS_ORGANIZATION_ACCESS_DENIED", "Access denied: Release belongs to a different organization.");
            }

            return Result<ClientUpdateReleaseContract>.Success(ClientUpdateContractAdapter.ToReleaseContract(release));
        }
    }

    public class GetUpdateReleasesByOrganizationQueryHandler : IQueryHandler<GetUpdateReleasesByOrganizationQuery, IReadOnlyList<ClientUpdateReleaseContract>>
    {
        private readonly IUpdateReleaseRepository _releaseRepository;
        private readonly IAuthorizationService _authorizationService;

        public GetUpdateReleasesByOrganizationQueryHandler(
            IUpdateReleaseRepository releaseRepository,
            IAuthorizationService authorizationService)
        {
            _releaseRepository = releaseRepository ?? throw new ArgumentNullException(nameof(releaseRepository));
            _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        }

        public async Task<Result<IReadOnlyList<ClientUpdateReleaseContract>>> HandleAsync(GetUpdateReleasesByOrganizationQuery query, CancellationToken cancellationToken = default)
        {
            if (query == null)
            {
                return Result<IReadOnlyList<ClientUpdateReleaseContract>>.Failure("INVALID_QUERY", "Query cannot be null.");
            }

            var principal = query.Principal ?? UserPrincipal.Anonymous;
            if (!principal.IsAuthenticated)
            {
                return Result<IReadOnlyList<ClientUpdateReleaseContract>>.Failure("PERMISSION_DENIED", "Authentication is required to query update releases.");
            }

            var authResult = await _authorizationService.AuthorizeAsync(principal, PermissionCatalog.ViewUpdates, null, cancellationToken);
            if (!authResult.IsAllowed)
            {
                authResult = await _authorizationService.AuthorizeAsync(principal, PermissionCatalog.ViewWorkstations, null, cancellationToken);
                if (!authResult.IsAllowed)
                {
                    return Result<IReadOnlyList<ClientUpdateReleaseContract>>.Failure("PERMISSION_DENIED", "Caller lacks required permissions.");
                }
            }

            var targetOrgId = query.OrganizationId != Guid.Empty ? query.OrganizationId : (principal.OrganizationId ?? Guid.Empty);
            if (targetOrgId == Guid.Empty)
            {
                return Result<IReadOnlyList<ClientUpdateReleaseContract>>.Failure("INVALID_ORGANIZATION", "Organization ID must be provided or available in principal context.");
            }

            if (principal.OrganizationId.HasValue &&
                principal.OrganizationId.Value != Guid.Empty &&
                principal.OrganizationId.Value != targetOrgId)
            {
                return Result<IReadOnlyList<ClientUpdateReleaseContract>>.Failure("CROSS_ORGANIZATION_ACCESS_DENIED", "Access denied: Cannot query releases for a different organization.");
            }

            var releases = await _releaseRepository.GetByOrganizationIdAsync(targetOrgId, false, cancellationToken);
            var contracts = releases.Select(ClientUpdateContractAdapter.ToReleaseContract).ToList();

            return Result<IReadOnlyList<ClientUpdateReleaseContract>>.Success(contracts);
        }
    }

    public class GetActiveUpdateReleaseQueryHandler : IQueryHandler<GetActiveUpdateReleaseQuery, ClientUpdateReleaseContract>
    {
        private readonly IUpdateReleaseRepository _releaseRepository;
        private readonly IAuthorizationService _authorizationService;

        public GetActiveUpdateReleaseQueryHandler(
            IUpdateReleaseRepository releaseRepository,
            IAuthorizationService authorizationService)
        {
            _releaseRepository = releaseRepository ?? throw new ArgumentNullException(nameof(releaseRepository));
            _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        }

        public async Task<Result<ClientUpdateReleaseContract>> HandleAsync(GetActiveUpdateReleaseQuery query, CancellationToken cancellationToken = default)
        {
            if (query == null)
            {
                return Result<ClientUpdateReleaseContract>.Failure("INVALID_QUERY", "Query cannot be null.");
            }

            var principal = query.Principal ?? UserPrincipal.Anonymous;
            if (!principal.IsAuthenticated)
            {
                return Result<ClientUpdateReleaseContract>.Failure("PERMISSION_DENIED", "Authentication is required to query active update release.");
            }

            var authResult = await _authorizationService.AuthorizeAsync(principal, PermissionCatalog.ViewUpdates, null, cancellationToken);
            if (!authResult.IsAllowed)
            {
                authResult = await _authorizationService.AuthorizeAsync(principal, PermissionCatalog.ViewWorkstations, null, cancellationToken);
                if (!authResult.IsAllowed)
                {
                    return Result<ClientUpdateReleaseContract>.Failure("PERMISSION_DENIED", "Caller lacks required permissions.");
                }
            }

            var targetOrgId = query.OrganizationId != Guid.Empty ? query.OrganizationId : (principal.OrganizationId ?? Guid.Empty);
            if (targetOrgId == Guid.Empty)
            {
                return Result<ClientUpdateReleaseContract>.Failure("INVALID_ORGANIZATION", "Organization ID must be provided or available in principal context.");
            }

            if (principal.OrganizationId.HasValue &&
                principal.OrganizationId.Value != Guid.Empty &&
                principal.OrganizationId.Value != targetOrgId)
            {
                return Result<ClientUpdateReleaseContract>.Failure("CROSS_ORGANIZATION_ACCESS_DENIED", "Access denied: Cannot query active release for a different organization.");
            }

            var activeRelease = await _releaseRepository.GetActiveReleaseAsync(targetOrgId, false, cancellationToken);
            if (activeRelease == null)
            {
                return Result<ClientUpdateReleaseContract>.Failure("NO_ACTIVE_RELEASE", $"No active update release found for organization '{targetOrgId}'.");
            }

            return Result<ClientUpdateReleaseContract>.Success(ClientUpdateContractAdapter.ToReleaseContract(activeRelease));
        }
    }
}
