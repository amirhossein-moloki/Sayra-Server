using System;
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
    public class SignUpdatePackageCommand : ICommand<ClientUpdatePackageMetadataContract>
    {
        public Guid PackageId { get; set; }
        public string? KeyId { get; set; }
        public UserPrincipal Principal { get; set; } = UserPrincipal.Anonymous;
    }

    public class VerifyUpdatePackageSignatureQuery : IQuery<UpdateSignatureVerificationResult>
    {
        public Guid PackageId { get; set; }
        public UserPrincipal Principal { get; set; } = UserPrincipal.Anonymous;
    }

    public class SignUpdatePackageCommandHandler : ICommandHandler<SignUpdatePackageCommand, ClientUpdatePackageMetadataContract>
    {
        private readonly IUpdatePackageRepository _packageRepository;
        private readonly IUpdateReleaseRepository _releaseRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IUpdateArtifactStorage _storage;
        private readonly IUpdateHashService _hashService;
        private readonly IUpdateSigningService _signingService;
        private readonly IAuthorizationService _authorizationService;
        private readonly ISecurityEventService _securityEventService;

        public SignUpdatePackageCommandHandler(
            IUpdatePackageRepository packageRepository,
            IUpdateReleaseRepository releaseRepository,
            IUnitOfWork unitOfWork,
            IUpdateArtifactStorage storage,
            IUpdateHashService hashService,
            IUpdateSigningService signingService,
            IAuthorizationService authorizationService,
            ISecurityEventService securityEventService)
        {
            _packageRepository = packageRepository ?? throw new ArgumentNullException(nameof(packageRepository));
            _releaseRepository = releaseRepository ?? throw new ArgumentNullException(nameof(releaseRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _hashService = hashService ?? throw new ArgumentNullException(nameof(hashService));
            _signingService = signingService ?? throw new ArgumentNullException(nameof(signingService));
            _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
            _securityEventService = securityEventService ?? throw new ArgumentNullException(nameof(securityEventService));
        }

        public async Task<Result<ClientUpdatePackageMetadataContract>> HandleAsync(SignUpdatePackageCommand command, CancellationToken cancellationToken = default)
        {
            if (command == null)
            {
                return Result<ClientUpdatePackageMetadataContract>.Failure("INVALID_COMMAND", "Command cannot be null.");
            }

            var principal = command.Principal ?? UserPrincipal.Anonymous;
            if (!principal.IsAuthenticated)
            {
                return Result<ClientUpdatePackageMetadataContract>.Failure("PERMISSION_DENIED", "Authentication is required to sign update packages.");
            }

            var authResult = await _authorizationService.AuthorizeAsync(principal, PermissionCatalog.ManageUpdates, null, cancellationToken);
            if (!authResult.IsAllowed)
            {
                authResult = await _authorizationService.AuthorizeAsync(principal, PermissionCatalog.ManageWorkstations, null, cancellationToken);
                if (!authResult.IsAllowed)
                {
                    return Result<ClientUpdatePackageMetadataContract>.Failure("PERMISSION_DENIED", "Caller lacks required administrative permissions to sign update packages.");
                }
            }

            var package = await _packageRepository.GetByIdAsync(command.PackageId, true, cancellationToken);
            if (package == null)
            {
                return Result<ClientUpdatePackageMetadataContract>.Failure("PACKAGE_NOT_FOUND", $"Update package '{command.PackageId}' was not found.");
            }

            var release = await _releaseRepository.GetByIdAsync(package.ReleaseId, true, cancellationToken);
            if (release == null)
            {
                return Result<ClientUpdatePackageMetadataContract>.Failure("RELEASE_NOT_FOUND", $"Associated update release '{package.ReleaseId}' was not found.");
            }

            if (principal.OrganizationId.HasValue &&
                principal.OrganizationId.Value != Guid.Empty &&
                principal.OrganizationId.Value != release.OrganizationId)
            {
                return Result<ClientUpdatePackageMetadataContract>.Failure("CROSS_ORGANIZATION_ACCESS_DENIED", "Access denied: Package belongs to a different organization.");
            }

            if (package.LifecycleState != UpdatePackageLifecycleState.Validated)
            {
                return Result<ClientUpdatePackageMetadataContract>.Failure("INVALID_LIFECYCLE_STATE", $"Package '{package.FileName}' must be in 'Validated' state to be signed. Current state is '{package.LifecycleState}'.");
            }

            // TOCTOU check: verify artifact exists in storage and stream digest matches authoritative SHA-256
            using var stream = await _storage.OpenReadStreamAsync(package.StorageKey, cancellationToken);
            if (stream == null)
            {
                return Result<ClientUpdatePackageMetadataContract>.Failure("STORAGE_ARTIFACT_NOT_FOUND", $"Artifact for storage key '{package.StorageKey}' was not found in storage.");
            }

            string computedHash = await _hashService.ComputeSha256Async(stream, cancellationToken);
            if (!string.Equals(computedHash, package.SHA256, StringComparison.OrdinalIgnoreCase))
            {
                // TOCTOU detected: artifact changed after validation. Quarantine package!
                package.SetVerificationStatus(UpdatePackageVerificationStatus.Quarantined);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                await _securityEventService.RecordSecurityEventAsync(
                    eventType: "UPDATE_SIGNING_FAILED",
                    actorId: principal.UserId,
                    actorType: "User",
                    deviceId: null,
                    organizationId: release.OrganizationId,
                    siteId: null,
                    resourceType: "UpdatePackage",
                    resourceId: package.Id,
                    action: "SIGN_ARTIFACT",
                    result: "FAILURE",
                    failureReason: $"TOCTOU integrity violation: computed hash '{computedHash}' does not match stored hash '{package.SHA256}'. Package quarantined.",
                    cancellationToken: cancellationToken);

                return Result<ClientUpdatePackageMetadataContract>.Failure("TOCTOU_INTEGRITY_VIOLATION", "Storage artifact hash does not match package authoritative hash. Package has been quarantined.");
            }

            try
            {
                await _securityEventService.RecordSecurityEventAsync(
                    eventType: "UPDATE_SIGNING_STARTED",
                    actorId: principal.UserId,
                    actorType: "User",
                    deviceId: null,
                    organizationId: release.OrganizationId,
                    siteId: null,
                    resourceType: "UpdatePackage",
                    resourceId: package.Id,
                    action: "SIGN_ARTIFACT",
                    result: "STARTED",
                    failureReason: null,
                    cancellationToken: cancellationToken);

                var signResult = await _signingService.SignPackageAsync(package, command.KeyId, cancellationToken);

                package.SignPackage(signResult.Signature, signResult.KeyId);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                await _securityEventService.RecordSecurityEventAsync(
                    eventType: "UPDATE_SIGNED",
                    actorId: principal.UserId,
                    actorType: "User",
                    deviceId: null,
                    organizationId: release.OrganizationId,
                    siteId: null,
                    resourceType: "UpdatePackage",
                    resourceId: package.Id,
                    action: "SIGN_ARTIFACT",
                    result: "SUCCESS",
                    failureReason: null,
                    cancellationToken: cancellationToken);

                var metadata = ClientUpdateContractAdapter.ToPackageMetadataContract(package);
                return Result<ClientUpdatePackageMetadataContract>.Success(metadata);
            }
            catch (InvalidDomainException ex)
            {
                await _securityEventService.RecordSecurityEventAsync(
                    eventType: "UPDATE_SIGNING_FAILED",
                    actorId: principal.UserId,
                    actorType: "User",
                    deviceId: null,
                    organizationId: release.OrganizationId,
                    siteId: null,
                    resourceType: "UpdatePackage",
                    resourceId: package.Id,
                    action: "SIGN_ARTIFACT",
                    result: "FAILURE",
                    failureReason: ex.Message,
                    cancellationToken: cancellationToken);

                return Result<ClientUpdatePackageMetadataContract>.Failure(ex.ErrorCode, ex.Message);
            }
            catch (Exception ex)
            {
                await _securityEventService.RecordSecurityEventAsync(
                    eventType: "UPDATE_SIGNING_FAILED",
                    actorId: principal.UserId,
                    actorType: "User",
                    deviceId: null,
                    organizationId: release.OrganizationId,
                    siteId: null,
                    resourceType: "UpdatePackage",
                    resourceId: package.Id,
                    action: "SIGN_ARTIFACT",
                    result: "FAILURE",
                    failureReason: ex.Message,
                    cancellationToken: cancellationToken);

                return Result<ClientUpdatePackageMetadataContract>.Failure("SIGNING_FAILED", $"Failed to sign package: {ex.Message}");
            }
        }
    }

    public class VerifyUpdatePackageSignatureQueryHandler : IQueryHandler<VerifyUpdatePackageSignatureQuery, UpdateSignatureVerificationResult>
    {
        private readonly IUpdatePackageRepository _packageRepository;
        private readonly IUpdateReleaseRepository _releaseRepository;
        private readonly IUpdateSigningService _signingService;
        private readonly IAuthorizationService _authorizationService;
        private readonly ISecurityEventService _securityEventService;

        public VerifyUpdatePackageSignatureQueryHandler(
            IUpdatePackageRepository packageRepository,
            IUpdateReleaseRepository releaseRepository,
            IUpdateSigningService signingService,
            IAuthorizationService authorizationService,
            ISecurityEventService securityEventService)
        {
            _packageRepository = packageRepository ?? throw new ArgumentNullException(nameof(packageRepository));
            _releaseRepository = releaseRepository ?? throw new ArgumentNullException(nameof(releaseRepository));
            _signingService = signingService ?? throw new ArgumentNullException(nameof(signingService));
            _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
            _securityEventService = securityEventService ?? throw new ArgumentNullException(nameof(securityEventService));
        }

        public async Task<Result<UpdateSignatureVerificationResult>> HandleAsync(VerifyUpdatePackageSignatureQuery query, CancellationToken cancellationToken = default)
        {
            if (query == null)
            {
                return Result<UpdateSignatureVerificationResult>.Failure("INVALID_QUERY", "Query cannot be null.");
            }

            var principal = query.Principal ?? UserPrincipal.Anonymous;
            if (!principal.IsAuthenticated)
            {
                return Result<UpdateSignatureVerificationResult>.Failure("PERMISSION_DENIED", "Authentication is required to verify update package signatures.");
            }

            var authResult = await _authorizationService.AuthorizeAsync(principal, PermissionCatalog.ViewUpdates, null, cancellationToken);
            if (!authResult.IsAllowed)
            {
                authResult = await _authorizationService.AuthorizeAsync(principal, PermissionCatalog.ViewWorkstations, null, cancellationToken);
                if (!authResult.IsAllowed)
                {
                    return Result<UpdateSignatureVerificationResult>.Failure("PERMISSION_DENIED", "Caller lacks required permissions.");
                }
            }

            var package = await _packageRepository.GetByIdAsync(query.PackageId, false, cancellationToken);
            if (package == null)
            {
                return Result<UpdateSignatureVerificationResult>.Failure("PACKAGE_NOT_FOUND", $"Update package '{query.PackageId}' was not found.");
            }

            var release = await _releaseRepository.GetByIdAsync(package.ReleaseId, false, cancellationToken);
            if (release == null)
            {
                return Result<UpdateSignatureVerificationResult>.Failure("RELEASE_NOT_FOUND", $"Associated update release '{package.ReleaseId}' was not found.");
            }

            if (principal.OrganizationId.HasValue &&
                principal.OrganizationId.Value != Guid.Empty &&
                principal.OrganizationId.Value != release.OrganizationId)
            {
                return Result<UpdateSignatureVerificationResult>.Failure("CROSS_ORGANIZATION_ACCESS_DENIED", "Access denied: Package belongs to a different organization.");
            }

            var verificationResult = await _signingService.VerifyPackageAsync(package, cancellationToken);

            if (verificationResult.IsValid)
            {
                await _securityEventService.RecordSecurityEventAsync(
                    eventType: "UPDATE_SIGNATURE_VERIFIED",
                    actorId: principal.UserId,
                    actorType: "User",
                    deviceId: null,
                    organizationId: release.OrganizationId,
                    siteId: null,
                    resourceType: "UpdatePackage",
                    resourceId: package.Id,
                    action: "VERIFY_SIGNATURE",
                    result: "SUCCESS",
                    failureReason: null,
                    cancellationToken: cancellationToken);
            }
            else
            {
                await _securityEventService.RecordSecurityEventAsync(
                    eventType: "UPDATE_SIGNATURE_VERIFICATION_FAILED",
                    actorId: principal.UserId,
                    actorType: "User",
                    deviceId: null,
                    organizationId: release.OrganizationId,
                    siteId: null,
                    resourceType: "UpdatePackage",
                    resourceId: package.Id,
                    action: "VERIFY_SIGNATURE",
                    result: "FAILURE",
                    failureReason: verificationResult.ErrorMessage,
                    cancellationToken: cancellationToken);
            }

            return Result<UpdateSignatureVerificationResult>.Success(verificationResult);
        }
    }
}
