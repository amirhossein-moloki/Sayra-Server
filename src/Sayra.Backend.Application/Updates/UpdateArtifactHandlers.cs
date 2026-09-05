using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
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
    public class UploadUpdatePackageCommandHandler : ICommandHandler<UploadUpdatePackageCommand, ClientUpdatePackageMetadataContract>
    {
        private readonly IUpdateReleaseRepository _releaseRepository;
        private readonly IUpdatePackageRepository _packageRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IUpdateArtifactStorage _artifactStorage;
        private readonly IUpdatePackageValidator _packageValidator;
        private readonly IUpdateHashService _hashService;
        private readonly IAuthorizationService _authorizationService;
        private readonly ISecurityEventService _securityEventService;
        private readonly ILogger<UploadUpdatePackageCommandHandler> _logger;

        public UploadUpdatePackageCommandHandler(
            IUpdateReleaseRepository releaseRepository,
            IUpdatePackageRepository packageRepository,
            IUnitOfWork unitOfWork,
            IUpdateArtifactStorage artifactStorage,
            IUpdatePackageValidator packageValidator,
            IUpdateHashService hashService,
            IAuthorizationService authorizationService,
            ISecurityEventService securityEventService,
            ILogger<UploadUpdatePackageCommandHandler> logger)
        {
            _releaseRepository = releaseRepository ?? throw new ArgumentNullException(nameof(releaseRepository));
            _packageRepository = packageRepository ?? throw new ArgumentNullException(nameof(packageRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _artifactStorage = artifactStorage ?? throw new ArgumentNullException(nameof(artifactStorage));
            _packageValidator = packageValidator ?? throw new ArgumentNullException(nameof(packageValidator));
            _hashService = hashService ?? throw new ArgumentNullException(nameof(hashService));
            _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
            _securityEventService = securityEventService ?? throw new ArgumentNullException(nameof(securityEventService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Result<ClientUpdatePackageMetadataContract>> HandleAsync(UploadUpdatePackageCommand command, CancellationToken cancellationToken)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));

            string? savedTempKey = null;
            string? promotedFinalKey = null;

            try
            {
                var authResult = await _authorizationService.AuthorizeAsync(command.Principal, PermissionCatalog.ManageUpdates, cancellationToken);
                if (!authResult.IsAllowed)
                {
                    authResult = await _authorizationService.AuthorizeAsync(command.Principal, PermissionCatalog.ManageWorkstations, cancellationToken);
                    if (!authResult.IsAllowed)
                    {
                        return Result<ClientUpdatePackageMetadataContract>.Failure("PERMISSION_DENIED", "Caller lacks required administrative permissions to upload update packages.");
                    }
                }

                var release = await _releaseRepository.GetByIdAsync(command.ReleaseId, true, cancellationToken);
                if (release == null)
                {
                    return Result<ClientUpdatePackageMetadataContract>.Failure("RELEASE_NOT_FOUND", $"Update release '{command.ReleaseId}' was not found.");
                }

                if (command.Principal.OrganizationId.HasValue &&
                    command.Principal.OrganizationId.Value != Guid.Empty &&
                    command.Principal.OrganizationId.Value != release.OrganizationId)
                {
                    _logger.LogWarning("Cross-organization update upload rejected for principal organization '{OrgA}' vs release organization '{OrgB}'.",
                        command.Principal.OrganizationId, release.OrganizationId);
                    return Result<ClientUpdatePackageMetadataContract>.Failure("CROSS_ORGANIZATION_ACCESS_DENIED", "Access denied: Update release belongs to another organization.");
                }

                if (release.Status == UpdateReleaseStatus.Revoked || release.IsImmutableState())
                {
                    return Result<ClientUpdatePackageMetadataContract>.Failure("RELEASE_IMMUTABLE", $"Update release '{release.Version}' is in state '{release.Status}' and cannot accept new packages.");
                }

                _packageValidator.ValidateFilename(command.FileName);

                var packageId = Guid.NewGuid();
                savedTempKey = await _artifactStorage.SaveTemporaryArtifactAsync(packageId, command.ContentStream, cancellationToken);

                string calculatedSha256;
                long calculatedSize;

                using (var tempReadStream = await _artifactStorage.OpenReadStreamAsync(savedTempKey, cancellationToken))
                {
                    calculatedSha256 = await _hashService.ComputeSha256Async(tempReadStream, cancellationToken);
                    calculatedSize = await _artifactStorage.GetArtifactSizeAsync(savedTempKey, cancellationToken);

                    _packageValidator.ValidateSize(calculatedSize);
                    _hashService.ValidateDeclaredHash(calculatedSha256, command.DeclaredSha256);

                    tempReadStream.Position = 0;
                    var validationResult = await _packageValidator.ValidateStructureAsync(tempReadStream, command.PackageType, cancellationToken);

                    if (validationResult.IsQuarantined)
                    {
                        var quarantinedPkg = UpdatePackage.Create(release.Id, command.FileName, calculatedSize, savedTempKey, command.PackageType);
                        quarantinedPkg.TransitionLifecycle(UpdatePackageLifecycleState.Quarantined);

                        release.AddPackage(quarantinedPkg);
                        await _packageRepository.AddAsync(quarantinedPkg, cancellationToken);
                        await _unitOfWork.SaveChangesAsync(cancellationToken);

                        await _securityEventService.RecordSecurityEventAsync(
                            eventType: "UPDATE_ARTIFACT_QUARANTINED",
                            actorId: command.Principal.UserId,
                            actorType: "User",
                            deviceId: null,
                            organizationId: release.OrganizationId,
                            siteId: null,
                            resourceType: "UpdatePackage",
                            resourceId: quarantinedPkg.Id,
                            action: "QUARANTINE_ARTIFACT",
                            result: "QUARANTINED",
                            failureReason: validationResult.ErrorMessage,
                            cancellationToken: cancellationToken);

                        return Result<ClientUpdatePackageMetadataContract>.Failure("PACKAGE_QUARANTINED", $"Package quarantined due to security violation: {validationResult.ErrorMessage}");
                    }

                    if (!validationResult.IsSuccess)
                    {
                        if (await _artifactStorage.ExistsAsync(savedTempKey, CancellationToken.None))
                        {
                            await _artifactStorage.DeleteArtifactAsync(savedTempKey, CancellationToken.None);
                        }

                        await _securityEventService.RecordSecurityEventAsync(
                            eventType: "UPDATE_ARTIFACT_VALIDATION_FAILED",
                            actorId: command.Principal.UserId,
                            actorType: "User",
                            deviceId: null,
                            organizationId: release.OrganizationId,
                            siteId: null,
                            resourceType: "UpdatePackage",
                            resourceId: packageId,
                            action: "VALIDATE_ARTIFACT",
                            result: "FAILURE",
                            failureReason: validationResult.ErrorMessage,
                            cancellationToken: cancellationToken);

                        return Result<ClientUpdatePackageMetadataContract>.Failure(validationResult.ErrorCode ?? "PACKAGE_VALIDATION_FAILED", validationResult.ErrorMessage ?? "Package structural validation failed.");
                    }
                }

                var package = UpdatePackage.Create(release.Id, command.FileName, calculatedSize, savedTempKey, command.PackageType);
                package.TransitionLifecycle(UpdatePackageLifecycleState.Uploaded);
                package.TransitionLifecycle(UpdatePackageLifecycleState.Validating);
                package.TransitionLifecycle(UpdatePackageLifecycleState.Validated);
                package.SetIntegrity(calculatedSha256);

                promotedFinalKey = $"packages/{release.Id:N}/{package.Id:N}.spk";
                await _artifactStorage.FinalizeArtifactAsync(savedTempKey, promotedFinalKey, cancellationToken);
                package.UpdateStorageKeyAndSize(promotedFinalKey, calculatedSize);

                release.AddPackage(package);
                await _packageRepository.AddAsync(package, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                await _securityEventService.RecordSecurityEventAsync(
                    eventType: "UPDATE_ARTIFACT_VALIDATED",
                    actorId: command.Principal.UserId,
                    actorType: "User",
                    deviceId: null,
                    organizationId: release.OrganizationId,
                    siteId: null,
                    resourceType: "UpdatePackage",
                    resourceId: package.Id,
                    action: "UPLOAD_AND_VALIDATE",
                    result: "SUCCESS",
                    failureReason: null,
                    cancellationToken: cancellationToken);

                var metadata = ClientUpdateContractAdapter.ToPackageMetadataContract(package);
                return Result<ClientUpdatePackageMetadataContract>.Success(metadata);
            }
            catch (InvalidDomainException domainEx)
            {
                await CleanupArtifactsAsync(savedTempKey, promotedFinalKey);
                _logger.LogWarning(domainEx, "Domain validation failure during update artifact upload: {Message}", domainEx.Message);
                return Result<ClientUpdatePackageMetadataContract>.Failure(domainEx.ErrorCode, domainEx.Message);
            }
            catch (Exception ex)
            {
                await CleanupArtifactsAsync(savedTempKey, promotedFinalKey);
                _logger.LogError(ex, "Failed to complete update artifact upload for release '{ReleaseId}'", command.ReleaseId);
                return Result<ClientUpdatePackageMetadataContract>.Failure("UPLOAD_FAILED", ex.Message);
            }
        }

        private async Task CleanupArtifactsAsync(string? tempKey, string? finalKey)
        {
            try
            {
                if (tempKey != null && await _artifactStorage.ExistsAsync(tempKey, CancellationToken.None))
                {
                    await _artifactStorage.DeleteArtifactAsync(tempKey, CancellationToken.None);
                }

                if (finalKey != null && await _artifactStorage.ExistsAsync(finalKey, CancellationToken.None))
                {
                    await _artifactStorage.DeleteArtifactAsync(finalKey, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clean up artifact storage during upload error recovery.");
            }
        }
    }

    public class ValidateUpdatePackageCommandHandler : ICommandHandler<ValidateUpdatePackageCommand, ClientUpdatePackageMetadataContract>
    {
        private readonly IUpdatePackageRepository _packageRepository;
        private readonly IUpdateReleaseRepository _releaseRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IUpdateArtifactStorage _artifactStorage;
        private readonly IUpdatePackageValidator _packageValidator;
        private readonly IUpdateHashService _hashService;
        private readonly IAuthorizationService _authorizationService;

        public ValidateUpdatePackageCommandHandler(
            IUpdatePackageRepository packageRepository,
            IUpdateReleaseRepository releaseRepository,
            IUnitOfWork unitOfWork,
            IUpdateArtifactStorage artifactStorage,
            IUpdatePackageValidator packageValidator,
            IUpdateHashService hashService,
            IAuthorizationService authorizationService)
        {
            _packageRepository = packageRepository ?? throw new ArgumentNullException(nameof(packageRepository));
            _releaseRepository = releaseRepository ?? throw new ArgumentNullException(nameof(releaseRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _artifactStorage = artifactStorage ?? throw new ArgumentNullException(nameof(artifactStorage));
            _packageValidator = packageValidator ?? throw new ArgumentNullException(nameof(packageValidator));
            _hashService = hashService ?? throw new ArgumentNullException(nameof(hashService));
            _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        }

        public async Task<Result<ClientUpdatePackageMetadataContract>> HandleAsync(ValidateUpdatePackageCommand command, CancellationToken cancellationToken)
        {
            try
            {
                var authResult = await _authorizationService.AuthorizeAsync(command.Principal, PermissionCatalog.ManageUpdates, cancellationToken);
                if (!authResult.IsAllowed)
                {
                    authResult = await _authorizationService.AuthorizeAsync(command.Principal, PermissionCatalog.ManageWorkstations, cancellationToken);
                    if (!authResult.IsAllowed)
                    {
                        return Result<ClientUpdatePackageMetadataContract>.Failure("PERMISSION_DENIED", "Caller lacks required permissions.");
                    }
                }

                var package = await _packageRepository.GetByIdAsync(command.PackageId, true, cancellationToken);
                if (package == null)
                {
                    return Result<ClientUpdatePackageMetadataContract>.Failure("PACKAGE_NOT_FOUND", $"Update package '{command.PackageId}' was not found.");
                }

                var release = await _releaseRepository.GetByIdAsync(package.ReleaseId, true, cancellationToken);
                if (release != null && command.Principal.OrganizationId.HasValue &&
                    command.Principal.OrganizationId.Value != Guid.Empty &&
                    command.Principal.OrganizationId.Value != release.OrganizationId)
                {
                    return Result<ClientUpdatePackageMetadataContract>.Failure("CROSS_ORGANIZATION_ACCESS_DENIED", "Access denied: Update package belongs to another organization.");
                }

                using var stream = await _artifactStorage.OpenReadStreamAsync(package.StorageKey, cancellationToken);
                var sha256 = await _hashService.ComputeSha256Async(stream, cancellationToken);
                var result = await _packageValidator.ValidateStructureAsync(stream, package.PackageType, cancellationToken);

                if (!result.IsSuccess)
                {
                    return Result<ClientUpdatePackageMetadataContract>.Failure(result.ErrorCode ?? "VALIDATION_FAILED", result.ErrorMessage ?? "Package validation failed.");
                }

                if (package.LifecycleState == UpdatePackageLifecycleState.Uploaded || package.LifecycleState == UpdatePackageLifecycleState.Validating)
                {
                    package.TransitionLifecycle(UpdatePackageLifecycleState.Validated);
                    package.SetIntegrity(sha256);
                }

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                var metadata = ClientUpdateContractAdapter.ToPackageMetadataContract(package);
                return Result<ClientUpdatePackageMetadataContract>.Success(metadata);
            }
            catch (InvalidDomainException domainEx)
            {
                return Result<ClientUpdatePackageMetadataContract>.Failure(domainEx.ErrorCode, domainEx.Message);
            }
            catch (Exception ex)
            {
                return Result<ClientUpdatePackageMetadataContract>.Failure("VALIDATION_FAILED", ex.Message);
            }
        }
    }

    public class GetUpdatePackageQueryHandler : IQueryHandler<GetUpdatePackageQuery, ClientUpdatePackageMetadataContract>
    {
        private readonly IUpdatePackageRepository _packageRepository;
        private readonly IUpdateReleaseRepository _releaseRepository;
        private readonly IAuthorizationService _authorizationService;

        public GetUpdatePackageQueryHandler(
            IUpdatePackageRepository packageRepository,
            IUpdateReleaseRepository releaseRepository,
            IAuthorizationService authorizationService)
        {
            _packageRepository = packageRepository ?? throw new ArgumentNullException(nameof(packageRepository));
            _releaseRepository = releaseRepository ?? throw new ArgumentNullException(nameof(releaseRepository));
            _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        }

        public async Task<Result<ClientUpdatePackageMetadataContract>> HandleAsync(GetUpdatePackageQuery query, CancellationToken cancellationToken)
        {
            try
            {
                var authResult = await _authorizationService.AuthorizeAsync(query.Principal, PermissionCatalog.ViewUpdates, cancellationToken);
                if (!authResult.IsAllowed)
                {
                    authResult = await _authorizationService.AuthorizeAsync(query.Principal, PermissionCatalog.ViewWorkstations, cancellationToken);
                    if (!authResult.IsAllowed)
                    {
                        return Result<ClientUpdatePackageMetadataContract>.Failure("PERMISSION_DENIED", "Caller lacks required permissions.");
                    }
                }

                var package = await _packageRepository.GetByIdAsync(query.PackageId, false, cancellationToken);
                if (package == null)
                {
                    return Result<ClientUpdatePackageMetadataContract>.Failure("PACKAGE_NOT_FOUND", $"Update package '{query.PackageId}' was not found.");
                }

                var release = await _releaseRepository.GetByIdAsync(package.ReleaseId, false, cancellationToken);
                if (release != null && query.Principal.OrganizationId.HasValue &&
                    query.Principal.OrganizationId.Value != Guid.Empty &&
                    query.Principal.OrganizationId.Value != release.OrganizationId)
                {
                    return Result<ClientUpdatePackageMetadataContract>.Failure("CROSS_ORGANIZATION_ACCESS_DENIED", "Access denied: Update package belongs to another organization.");
                }

                var metadata = ClientUpdateContractAdapter.ToPackageMetadataContract(package);
                return Result<ClientUpdatePackageMetadataContract>.Success(metadata);
            }
            catch (InvalidDomainException domainEx)
            {
                return Result<ClientUpdatePackageMetadataContract>.Failure(domainEx.ErrorCode, domainEx.Message);
            }
            catch (Exception ex)
            {
                return Result<ClientUpdatePackageMetadataContract>.Failure("GET_PACKAGE_FAILED", ex.Message);
            }
        }
    }

    public class DeleteUpdatePackageCommandHandler : ICommandHandler<DeleteUpdatePackageCommand, bool>
    {
        private readonly IUpdatePackageRepository _packageRepository;
        private readonly IUpdateReleaseRepository _releaseRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IUpdateArtifactStorage _artifactStorage;
        private readonly IAuthorizationService _authorizationService;

        public DeleteUpdatePackageCommandHandler(
            IUpdatePackageRepository packageRepository,
            IUpdateReleaseRepository releaseRepository,
            IUnitOfWork unitOfWork,
            IUpdateArtifactStorage artifactStorage,
            IAuthorizationService authorizationService)
        {
            _packageRepository = packageRepository ?? throw new ArgumentNullException(nameof(packageRepository));
            _releaseRepository = releaseRepository ?? throw new ArgumentNullException(nameof(releaseRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _artifactStorage = artifactStorage ?? throw new ArgumentNullException(nameof(artifactStorage));
            _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        }

        public async Task<Result<bool>> HandleAsync(DeleteUpdatePackageCommand command, CancellationToken cancellationToken)
        {
            try
            {
                var authResult = await _authorizationService.AuthorizeAsync(command.Principal, PermissionCatalog.ManageUpdates, cancellationToken);
                if (!authResult.IsAllowed)
                {
                    authResult = await _authorizationService.AuthorizeAsync(command.Principal, PermissionCatalog.ManageWorkstations, cancellationToken);
                    if (!authResult.IsAllowed)
                    {
                        return Result<bool>.Failure("PERMISSION_DENIED", "Caller lacks required permissions.");
                    }
                }

                var package = await _packageRepository.GetByIdAsync(command.PackageId, true, cancellationToken);
                if (package == null)
                {
                    return Result<bool>.Success(false);
                }

                var release = await _releaseRepository.GetByIdAsync(package.ReleaseId, true, cancellationToken);
                if (release != null && command.Principal.OrganizationId.HasValue &&
                    command.Principal.OrganizationId.Value != Guid.Empty &&
                    command.Principal.OrganizationId.Value != release.OrganizationId)
                {
                    return Result<bool>.Failure("CROSS_ORGANIZATION_ACCESS_DENIED", "Access denied: Update package belongs to another organization.");
                }

                if (package.IsImmutableArtifactState())
                {
                    return Result<bool>.Failure("PACKAGE_IMMUTABLE", $"Update package '{package.FileName}' is immutable and cannot be deleted.");
                }

                if (await _artifactStorage.ExistsAsync(package.StorageKey, cancellationToken))
                {
                    await _artifactStorage.DeleteArtifactAsync(package.StorageKey, cancellationToken);
                }

                _packageRepository.Delete(package);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return Result<bool>.Success(true);
            }
            catch (InvalidDomainException domainEx)
            {
                return Result<bool>.Failure(domainEx.ErrorCode, domainEx.Message);
            }
            catch (Exception ex)
            {
                return Result<bool>.Failure("DELETE_PACKAGE_FAILED", ex.Message);
            }
        }
    }
}
