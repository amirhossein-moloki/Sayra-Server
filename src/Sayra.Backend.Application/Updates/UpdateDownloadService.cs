using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Exceptions;

#nullable enable

namespace Sayra.Backend.Application.Updates
{
    public class UpdateDownloadService : IUpdateDownloadService
    {
        private readonly IUpdatePackageRepository _packageRepository;
        private readonly IUpdateReleaseRepository _releaseRepository;
        private readonly IRepository<Workstation> _workstationRepository;
        private readonly IUpdateEligibilityService _eligibilityService;
        private readonly IUpdateArtifactStorage _storage;
        private readonly ISecurityEventService? _securityEventService;
        private readonly ILogger<UpdateDownloadService> _logger;

        public UpdateDownloadService(
            IUpdatePackageRepository packageRepository,
            IUpdateReleaseRepository releaseRepository,
            IRepository<Workstation> workstationRepository,
            IUpdateEligibilityService eligibilityService,
            IUpdateArtifactStorage storage,
            ILogger<UpdateDownloadService> logger,
            ISecurityEventService? securityEventService = null)
        {
            _packageRepository = packageRepository ?? throw new ArgumentNullException(nameof(packageRepository));
            _releaseRepository = releaseRepository ?? throw new ArgumentNullException(nameof(releaseRepository));
            _workstationRepository = workstationRepository ?? throw new ArgumentNullException(nameof(workstationRepository));
            _eligibilityService = eligibilityService ?? throw new ArgumentNullException(nameof(eligibilityService));
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _securityEventService = securityEventService;
        }

        public async Task<UpdateDownloadPreparation> PrepareDownloadAsync(
            UpdateDownloadRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (request.Principal == null || !request.Principal.IsAuthenticated)
            {
                _logger.LogWarning("UPDATE_DOWNLOAD_REJECTED: Unauthenticated download request for package '{PackageId}'.", request.PackageId);
                return UpdateDownloadPreparation.Failure(401, "UNAUTHORIZED", "Authentication is required to download update packages.");
            }

            if (request.PackageId == Guid.Empty)
            {
                return UpdateDownloadPreparation.Failure(400, "INVALID_PACKAGE_ID", "Package ID cannot be empty.");
            }

            // 1. Resolve Workstation Identity from Principal Context
            Workstation? workstation = null;

            if (!string.IsNullOrWhiteSpace(request.Principal.PcId))
            {
                string pcId = request.Principal.PcId.Trim().ToUpperInvariant();
                var matches = await _workstationRepository.FindAsync(
                    w => w.PcId == pcId,
                    track: false,
                    cancellationToken: cancellationToken);
                workstation = matches.FirstOrDefault();
            }
            else if (request.Principal.UserId.HasValue)
            {
                workstation = await _workstationRepository.GetByIdAsync(
                    request.Principal.UserId.Value,
                    track: false,
                    cancellationToken: cancellationToken);
            }

            if (workstation == null)
            {
                _logger.LogWarning("UPDATE_DOWNLOAD_REJECTED: Workstation identity could not be resolved for principal (PcId: {PcId}, UserId: {UserId}).",
                    request.Principal.PcId, request.Principal.UserId);
                return UpdateDownloadPreparation.Failure(404, EligibilityReasonCodes.WorkstationNotFound, "Bound workstation identity was not found.");
            }

            if (workstation.IsDeactivated || workstation.IsDisabled)
            {
                _logger.LogWarning("UPDATE_DOWNLOAD_REJECTED: Workstation '{PcId}' ({WorkstationId}) is disabled or deactivated.",
                    workstation.PcId, workstation.Id);
                return UpdateDownloadPreparation.Failure(403, EligibilityReasonCodes.WorkstationDeactivated, $"Workstation '{workstation.PcId}' is disabled or deactivated.");
            }

            if (!workstation.OrganizationEntityId.HasValue || workstation.OrganizationEntityId.Value == Guid.Empty)
            {
                _logger.LogWarning("UPDATE_DOWNLOAD_REJECTED: Workstation '{PcId}' is not assigned to an organization.", workstation.PcId);
                return UpdateDownloadPreparation.Failure(403, EligibilityReasonCodes.OrganizationMismatch, $"Workstation '{workstation.PcId}' is not assigned to an organization.");
            }

            // Tenant Boundary Check
            if (request.Principal.OrganizationId.HasValue &&
                request.Principal.OrganizationId.Value != Guid.Empty &&
                request.Principal.OrganizationId.Value != workstation.OrganizationEntityId.Value)
            {
                _logger.LogWarning("UPDATE_DOWNLOAD_REJECTED: Organization boundary mismatch for workstation '{PcId}' (Principal Org: {PrincipalOrg}, Workstation Org: {WorkstationOrg}).",
                    workstation.PcId, request.Principal.OrganizationId, workstation.OrganizationEntityId);
                return UpdateDownloadPreparation.Failure(403, EligibilityReasonCodes.OrganizationMismatch, "Cross-organization access is strictly forbidden.");
            }

            Guid organizationId = workstation.OrganizationEntityId.Value;

            // 2. Resolve Package & Release Metadata
            var package = await _packageRepository.GetByIdAsync(request.PackageId, track: false, cancellationToken: cancellationToken);
            if (package == null)
            {
                _logger.LogWarning("UPDATE_DOWNLOAD_REJECTED: Package '{PackageId}' was not found.", request.PackageId);
                return UpdateDownloadPreparation.Failure(404, "PACKAGE_NOT_FOUND", $"Update package '{request.PackageId}' was not found.");
            }

            var release = await _releaseRepository.GetByIdAsync(package.ReleaseId, track: false, cancellationToken: cancellationToken);
            if (release == null)
            {
                _logger.LogWarning("UPDATE_DOWNLOAD_REJECTED: Release '{ReleaseId}' for package '{PackageId}' was not found.", package.ReleaseId, package.Id);
                return UpdateDownloadPreparation.Failure(404, "RELEASE_NOT_FOUND", $"Release for package '{request.PackageId}' was not found.");
            }

            // Tenant Isolation Guard (Prevent enumeration/download across organizations)
            if (release.OrganizationId != organizationId)
            {
                _logger.LogWarning("UPDATE_DOWNLOAD_REJECTED: Cross-organization download attempt by workstation '{PcId}' (Org '{WorkstationOrg}') for release '{Version}' (Org '{ReleaseOrg}').",
                    workstation.PcId, organizationId, release.Version, release.OrganizationId);
                return UpdateDownloadPreparation.Failure(404, "PACKAGE_NOT_FOUND", $"Update package '{request.PackageId}' was not found.");
            }

            // 3. Re-Validate Lifecycle State & Verification Status
            if (release.Status != UpdateReleaseStatus.Published && release.Status != UpdateReleaseStatus.Active)
            {
                _logger.LogWarning("UPDATE_DOWNLOAD_REJECTED: Release '{Version}' status is '{Status}', not Published or Active.",
                    release.Version, release.Status);
                return UpdateDownloadPreparation.Failure(403, EligibilityReasonCodes.ReleaseNotActive, $"Release '{release.Version}' is in state '{release.Status}' and cannot be downloaded.");
            }

            if (package.LifecycleState != UpdatePackageLifecycleState.Ready)
            {
                _logger.LogWarning("UPDATE_DOWNLOAD_REJECTED: Package '{PackageId}' lifecycle state is '{State}', not Ready.",
                    package.Id, package.LifecycleState);
                return UpdateDownloadPreparation.Failure(403, EligibilityReasonCodes.PackageNotReady, $"Package '{package.Id}' state is '{package.LifecycleState}' and is not ready for download.");
            }

            if (package.VerificationStatus != UpdatePackageVerificationStatus.Valid)
            {
                _logger.LogWarning("UPDATE_DOWNLOAD_REJECTED: Package '{PackageId}' verification status is '{Status}', not Valid.",
                    package.Id, package.VerificationStatus);
                return UpdateDownloadPreparation.Failure(403, EligibilityReasonCodes.SignatureInvalid, $"Package '{package.Id}' cryptographic verification is incomplete.");
            }

            if (string.IsNullOrWhiteSpace(package.SHA256) || string.IsNullOrWhiteSpace(package.Signature))
            {
                _logger.LogWarning("UPDATE_DOWNLOAD_REJECTED: Package '{PackageId}' is missing SHA256 checksum or RSA signature.", package.Id);
                return UpdateDownloadPreparation.Failure(403, EligibilityReasonCodes.SignatureInvalid, $"Package '{package.Id}' missing checksum or digital signature.");
            }

            if (package.Size <= 0 || string.IsNullOrWhiteSpace(package.StorageKey))
            {
                _logger.LogWarning("UPDATE_DOWNLOAD_REJECTED: Package '{PackageId}' contains invalid size ({Size}) or empty storage key.", package.Id, package.Size);
                return UpdateDownloadPreparation.Failure(403, "INVALID_PACKAGE_METADATA", "Package storage metadata is invalid.");
            }

            // 4. Server-Authoritative Eligibility Revalidation at Download Time
            var eligibility = await _eligibilityService.EvaluateWorkstationEligibilityAsync(
                workstation.Id,
                request.ReportedVersion,
                request.OsVersion,
                request.Architecture,
                cancellationToken);

            if (!eligibility.IsEligible || eligibility.PackageContract?.PackageId != package.Id)
            {
                _logger.LogWarning("UPDATE_DOWNLOAD_REJECTED: Workstation '{PcId}' is not eligible for package '{PackageId}'. Reason: {ReasonCode} - {Details}",
                    workstation.PcId, package.Id, eligibility.ReasonCode, eligibility.ReasonDetails);
                return UpdateDownloadPreparation.Failure(403, eligibility.ReasonCode ?? EligibilityReasonCodes.RolloutNotEligible, $"Workstation '{workstation.PcId}' is not eligible to download this package. Reason: {eligibility.ReasonDetails}");
            }

            // 5. Verify Artifact Storage Object Existence
            bool storageExists = await _storage.ExistsAsync(package.StorageKey, cancellationToken);
            if (!storageExists)
            {
                _logger.LogError("UPDATE_DOWNLOAD_FAILED: Physical storage artifact for package '{PackageId}' (StorageKey: '{StorageKey}') was not found.",
                    package.Id, package.StorageKey);
                return UpdateDownloadPreparation.Failure(503, "STORAGE_UNAVAILABLE", "Artifact storage object is currently unavailable.");
            }

            long actualArtifactSize = await _storage.GetArtifactSizeAsync(package.StorageKey, cancellationToken);
            long servedTotalSize = actualArtifactSize > 0 ? actualArtifactSize : package.Size;

            // 6. Range Header Parsing & Validation
            var range = UpdateDownloadRangeHelper.ParseRangeHeader(request.RangeHeader, servedTotalSize);
            if (range.IsRangeRequest && !range.IsSatisfiable)
            {
                _logger.LogWarning("UPDATE_DOWNLOAD_INVALID_RANGE: Workstation '{PcId}' requested unsatisfiable range '{Range}' for artifact size {Size}.",
                    workstation.PcId, request.RangeHeader, servedTotalSize);

                if (_securityEventService != null)
                {
                    await _securityEventService.RecordSecurityEventAsync(
                        eventType: "UPDATE_DOWNLOAD_INVALID_RANGE",
                        actorId: request.Principal.UserId,
                        actorType: request.Principal.UserId.HasValue ? "USER" : "WORKSTATION",
                        deviceId: workstation.PcId,
                        organizationId: organizationId,
                        siteId: workstation.SiteEntityId,
                        resourceType: "UpdatePackage",
                        resourceId: package.Id,
                        action: "DOWNLOAD_RANGE",
                        result: "FAILURE",
                        failureReason: $"Unsatisfiable range '{request.RangeHeader}'",
                        cancellationToken: cancellationToken);
                }

                return UpdateDownloadPreparation.Failure(416, "INVALID_RANGE", $"Requested Range '{request.RangeHeader}' is not satisfiable.", range);
            }

            // 7. Open Stream & Seek if Range Offset > 0
            Stream contentStream;
            try
            {
                contentStream = await _storage.OpenReadStreamAsync(package.StorageKey, cancellationToken);
                if (range.IsRangeRequest && range.IsSatisfiable && range.Start > 0 && contentStream.CanSeek)
                {
                    contentStream.Seek(range.Start, SeekOrigin.Begin);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UPDATE_DOWNLOAD_ERROR: Failed to open read stream for package '{PackageId}' storage key '{StorageKey}'.",
                    package.Id, package.StorageKey);
                return UpdateDownloadPreparation.Failure(503, "STORAGE_UNAVAILABLE", "Failed to open artifact read stream.");
            }

            // 8. Record Security Audit Log
            if (_securityEventService != null)
            {
                string eventType = range.IsRangeRequest ? "UPDATE_DOWNLOAD_RANGE_STARTED" : "UPDATE_DOWNLOAD_STARTED";
                await _securityEventService.RecordSecurityEventAsync(
                    eventType: eventType,
                    actorId: request.Principal.UserId,
                    actorType: request.Principal.UserId.HasValue ? "USER" : "WORKSTATION",
                    deviceId: workstation.PcId,
                    organizationId: organizationId,
                    siteId: workstation.SiteEntityId,
                    resourceType: "UpdatePackage",
                    resourceId: package.Id,
                    action: "DOWNLOAD",
                    result: "SUCCESS",
                    failureReason: null,
                    cancellationToken: cancellationToken);
            }

            _logger.LogInformation("UPDATE_DOWNLOAD_AUTHORIZED: Serving package '{PackageId}' (Version: '{Version}', File: '{FileName}', Size: {Size}, ServedLength: {Length}, RangeRequest: {IsRange}) to Workstation '{PcId}'",
                package.Id, release.Version, package.FileName, servedTotalSize, range.ServedLength, range.IsRangeRequest, workstation.PcId);

            return UpdateDownloadPreparation.Success(
                package.Id,
                release.Id,
                release.Version,
                package.FileName,
                servedTotalSize,
                range,
                package.SHA256,
                package.Signature,
                package.StorageKey,
                contentStream);
        }
    }
}
