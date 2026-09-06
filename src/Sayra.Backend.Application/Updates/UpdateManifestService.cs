using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;

#nullable enable

namespace Sayra.Backend.Application.Updates
{
    public class UpdateManifestService : IUpdateManifestService
    {
        private readonly IUpdateEligibilityService _eligibilityService;
        private readonly IRepository<Workstation> _workstationRepository;
        private readonly ISecurityEventService? _securityEventService;
        private readonly ILogger<UpdateManifestService> _logger;

        public UpdateManifestService(
            IUpdateEligibilityService eligibilityService,
            IRepository<Workstation> workstationRepository,
            ILogger<UpdateManifestService> logger,
            ISecurityEventService? securityEventService = null)
        {
            _eligibilityService = eligibilityService ?? throw new ArgumentNullException(nameof(eligibilityService));
            _workstationRepository = workstationRepository ?? throw new ArgumentNullException(nameof(workstationRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _securityEventService = securityEventService;
        }

        public async Task<UpdateManifestResult> GetManifestAsync(
            UpdateManifestRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (request.Principal == null || !request.Principal.IsAuthenticated)
            {
                _logger.LogWarning("UPDATE_MANIFEST_REJECTED: Unauthenticated request received.");
                return UpdateManifestResult.NotAvailable("UNAUTHORIZED", "Authentication is required to request update manifest.");
            }

            // 1. Resolve Workstation Identity from Authenticated Context
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
                _logger.LogWarning("UPDATE_MANIFEST_REJECTED: Workstation identity could not be resolved for principal (PcId: {PcId}, UserId: {UserId}).",
                    request.Principal.PcId, request.Principal.UserId);
                return UpdateManifestResult.NotAvailable(EligibilityReasonCodes.WorkstationNotFound, "Bound workstation identity was not found.");
            }

            if (workstation.IsDeactivated || workstation.IsDisabled)
            {
                _logger.LogWarning("UPDATE_MANIFEST_REJECTED: Workstation '{PcId}' ({WorkstationId}) is disabled or deactivated.",
                    workstation.PcId, workstation.Id);
                return UpdateManifestResult.NotAvailable(EligibilityReasonCodes.WorkstationDeactivated, $"Workstation '{workstation.PcId}' is disabled or deactivated.");
            }

            if (!workstation.OrganizationEntityId.HasValue || workstation.OrganizationEntityId.Value == Guid.Empty)
            {
                _logger.LogWarning("UPDATE_MANIFEST_REJECTED: Workstation '{PcId}' is not assigned to an organization.", workstation.PcId);
                return UpdateManifestResult.NotAvailable(EligibilityReasonCodes.OrganizationMismatch, $"Workstation '{workstation.PcId}' is not assigned to an organization.");
            }

            // Tenant Isolation Guard
            if (request.Principal.OrganizationId.HasValue &&
                request.Principal.OrganizationId.Value != Guid.Empty &&
                request.Principal.OrganizationId.Value != workstation.OrganizationEntityId.Value)
            {
                _logger.LogWarning("UPDATE_MANIFEST_REJECTED: Organization boundary mismatch for workstation '{PcId}' (Principal Org: {PrincipalOrg}, Workstation Org: {WorkstationOrg}).",
                    workstation.PcId, request.Principal.OrganizationId, workstation.OrganizationEntityId);
                return UpdateManifestResult.NotAvailable(EligibilityReasonCodes.OrganizationMismatch, "Cross-organization access is strictly forbidden.");
            }

            Guid organizationId = workstation.OrganizationEntityId.Value;

            _logger.LogInformation("UPDATE_MANIFEST_REQUESTED: Workstation '{PcId}' ({WorkstationId}) in Org '{OrgId}' requesting update discovery. ReportedVersion: {ReportedVersion}, OS: {OS}, Arch: {Arch}",
                workstation.PcId, workstation.Id, organizationId, request.ReportedVersion, request.OsVersion, request.Architecture);

            // 2. Evaluate Server-Authoritative Eligibility
            var eligibility = await _eligibilityService.EvaluateWorkstationEligibilityAsync(
                workstation.Id,
                request.ReportedVersion,
                request.OsVersion,
                request.Architecture,
                cancellationToken);

            if (!eligibility.IsEligible || eligibility.ReleaseContract == null || eligibility.PackageContract == null)
            {
                _logger.LogInformation("UPDATE_MANIFEST_NOT_AVAILABLE: Workstation '{PcId}' is not eligible for update. ReasonCode: {ReasonCode}, Details: {Details}",
                    workstation.PcId, eligibility.ReasonCode, eligibility.ReasonDetails);

                return UpdateManifestResult.NotAvailable(eligibility.ReasonCode, eligibility.ReasonDetails);
            }

            // 3. Defense-In-Depth Precondition Validation
            var releaseContract = eligibility.ReleaseContract;
            var packageContract = eligibility.PackageContract;

            if (releaseContract.OrganizationId != organizationId)
            {
                _logger.LogError("UPDATE_MANIFEST_REJECTED: Selected release Org '{ReleaseOrg}' does not match workstation Org '{WorkstationOrg}'.",
                    releaseContract.OrganizationId, organizationId);
                return UpdateManifestResult.NotAvailable(EligibilityReasonCodes.OrganizationMismatch, "Selected release organization mismatch.");
            }

            if (releaseContract.Status != UpdateReleaseStatus.Published.ToString() &&
                releaseContract.Status != UpdateReleaseStatus.Active.ToString())
            {
                _logger.LogWarning("UPDATE_MANIFEST_NOT_AVAILABLE: Selected release '{Version}' status is '{Status}', not Published or Active.",
                    releaseContract.Version, releaseContract.Status);
                return UpdateManifestResult.NotAvailable(EligibilityReasonCodes.ReleaseNotActive, $"Release '{releaseContract.Version}' is not active or published.");
            }

            if (string.IsNullOrWhiteSpace(packageContract.ChecksumSha256) || string.IsNullOrWhiteSpace(packageContract.Signature))
            {
                _logger.LogWarning("UPDATE_MANIFEST_NOT_AVAILABLE: Selected package '{PackageId}' for release '{Version}' is missing checksum or RSA signature.",
                    packageContract.PackageId, releaseContract.Version);
                return UpdateManifestResult.NotAvailable(EligibilityReasonCodes.SignatureInvalid, "Package checksum or signature is invalid.");
            }

            // 4. Construct Public Download URL and Client Manifest Contract
            string relativeDownloadUrl = string.Format(ClientUpdateProtocolConstants.DownloadRoutePattern, packageContract.PackageId);
            string packageUrl = string.IsNullOrWhiteSpace(request.DownloadBaseUrl)
                ? relativeDownloadUrl
                : $"{request.DownloadBaseUrl.TrimEnd('/')}{relativeDownloadUrl}";

            var manifestContract = new ClientUpdateManifestContract
            {
                Version = releaseContract.Version,
                ReleaseNotes = releaseContract.ReleaseNotes,
                PackageUrl = packageUrl,
                Checksum = packageContract.ChecksumSha256,
                Signature = packageContract.Signature,
                IsMandatory = eligibility.IsMandatory,
                MinimumSupportedVersion = eligibility.MinimumSupportedVersion,
                FileSize = packageContract.Size,
                PackageType = packageContract.PackageType
            };

            _logger.LogInformation("UPDATE_MANIFEST_AVAILABLE: Update manifest generated for Workstation '{PcId}' -> Release '{Version}', PackageId: {PackageId}, Mandatory: {Mandatory}",
                workstation.PcId, manifestContract.Version, packageContract.PackageId, manifestContract.IsMandatory);

            if (_securityEventService != null)
            {
                await _securityEventService.RecordSecurityEventAsync(
                    eventType: "UPDATE_MANIFEST_DISCOVERED",
                    actorId: request.Principal.UserId,
                    actorType: request.Principal.UserId.HasValue ? "USER" : "WORKSTATION",
                    deviceId: workstation.PcId,
                    organizationId: organizationId,
                    siteId: workstation.SiteEntityId,
                    resourceType: "UpdateRelease",
                    resourceId: releaseContract.ReleaseId,
                    action: "DISCOVER",
                    result: "SUCCESS",
                    failureReason: null,
                    cancellationToken: cancellationToken);
            }

            return UpdateManifestResult.Available(manifestContract, eligibility.ReasonDetails);
        }
    }
}
