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
using Sayra.Backend.Shared;

#nullable enable

namespace Sayra.Backend.Application.Updates
{
    public class UpdateOperationalStatusDto
    {
        public string Subsystem { get; set; } = "SoftwareUpdatePlatform";
        public string OverallStatus { get; set; } = "Healthy";
        public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
        public Guid OrganizationId { get; set; }

        public UpdateSubsystemHealthDto Storage { get; set; } = new();
        public UpdateSubsystemHealthDto SigningProvider { get; set; } = new();
        public UpdateReleaseSummaryDto? ActiveRelease { get; set; }
        public int TotalReleasesCount { get; set; }
        public int TotalTargetsCount { get; set; }
    }

    public class UpdateSubsystemHealthDto
    {
        public string Component { get; set; } = string.Empty;
        public string Status { get; set; } = "Healthy";
        public string Description { get; set; } = string.Empty;
        public string? ActiveKeyId { get; set; }
        public string? Algorithm { get; set; }
    }

    public class UpdateReleaseSummaryDto
    {
        public Guid ReleaseId { get; set; }
        public string Version { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string ReleaseType { get; set; } = string.Empty;
        public DateTime? PublishedAt { get; set; }
        public Guid? PackageId { get; set; }
        public string? PackageState { get; set; }
        public long PackageSize { get; set; }
    }

    public class GetUpdateOperationalStatusQuery : IQuery<UpdateOperationalStatusDto>
    {
        public Guid OrganizationId { get; set; }
        public UserPrincipal Principal { get; set; } = UserPrincipal.Anonymous;
    }

    public class GetUpdateOperationalStatusQueryHandler : IQueryHandler<GetUpdateOperationalStatusQuery, UpdateOperationalStatusDto>
    {
        private readonly IUpdateReleaseRepository _releaseRepository;
        private readonly IUpdateTargetRepository _targetRepository;
        private readonly IUpdateArtifactStorage _storage;
        private readonly IUpdateSigningKeyProvider _signingKeyProvider;
        private readonly IAuthorizationService _authorizationService;

        public GetUpdateOperationalStatusQueryHandler(
            IUpdateReleaseRepository releaseRepository,
            IUpdateTargetRepository targetRepository,
            IUpdateArtifactStorage storage,
            IUpdateSigningKeyProvider signingKeyProvider,
            IAuthorizationService authorizationService)
        {
            _releaseRepository = releaseRepository ?? throw new ArgumentNullException(nameof(releaseRepository));
            _targetRepository = targetRepository ?? throw new ArgumentNullException(nameof(targetRepository));
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _signingKeyProvider = signingKeyProvider ?? throw new ArgumentNullException(nameof(signingKeyProvider));
            _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        }

        public async Task<Result<UpdateOperationalStatusDto>> HandleAsync(
            GetUpdateOperationalStatusQuery query,
            CancellationToken cancellationToken = default)
        {
            if (query == null)
            {
                return Result<UpdateOperationalStatusDto>.Failure("INVALID_QUERY", "Query cannot be null.");
            }

            var principal = query.Principal ?? UserPrincipal.Anonymous;
            if (!principal.IsAuthenticated)
            {
                return Result<UpdateOperationalStatusDto>.Failure("PERMISSION_DENIED", "Authentication is required to query operational status.");
            }

            var authResult = await _authorizationService.AuthorizeAsync(principal, PermissionCatalog.ViewUpdates, null, cancellationToken);
            if (!authResult.IsAllowed)
            {
                authResult = await _authorizationService.AuthorizeAsync(principal, PermissionCatalog.ViewWorkstations, null, cancellationToken);
                if (!authResult.IsAllowed)
                {
                    return Result<UpdateOperationalStatusDto>.Failure("PERMISSION_DENIED", "Caller lacks required administrative permissions.");
                }
            }

            var targetOrgId = query.OrganizationId != Guid.Empty ? query.OrganizationId : (principal.OrganizationId ?? Guid.Empty);
            if (targetOrgId == Guid.Empty)
            {
                return Result<UpdateOperationalStatusDto>.Failure("INVALID_ORGANIZATION", "Organization ID must be provided or available in principal context.");
            }

            if (principal.OrganizationId.HasValue &&
                principal.OrganizationId.Value != Guid.Empty &&
                principal.OrganizationId.Value != targetOrgId)
            {
                return Result<UpdateOperationalStatusDto>.Failure("CROSS_ORGANIZATION_ACCESS_DENIED", "Access denied: Cannot query operational status for a different organization.");
            }

            var report = new UpdateOperationalStatusDto
            {
                OrganizationId = targetOrgId,
                CheckedAt = DateTime.UtcNow
            };

            // 1. Storage Health Check
            try
            {
                bool probe = await _storage.ExistsAsync("__sayra_health_probe__", cancellationToken);
                report.Storage = new UpdateSubsystemHealthDto
                {
                    Component = "ArtifactStorage",
                    Status = "Healthy",
                    Description = "Artifact storage repository is online."
                };
            }
            catch (Exception ex)
            {
                report.Storage = new UpdateSubsystemHealthDto
                {
                    Component = "ArtifactStorage",
                    Status = "Unhealthy",
                    Description = $"Artifact storage failure: {ex.Message}"
                };
                report.OverallStatus = "Degraded";
            }

            // 2. Signing Provider Health Check
            try
            {
                var activeKey = await _signingKeyProvider.GetActiveKeyAsync(cancellationToken);
                report.SigningProvider = new UpdateSubsystemHealthDto
                {
                    Component = "SigningProvider",
                    Status = "Healthy",
                    Description = "Active signing key registry is online.",
                    ActiveKeyId = activeKey?.KeyId,
                    Algorithm = activeKey?.Algorithm
                };
            }
            catch (Exception ex)
            {
                report.SigningProvider = new UpdateSubsystemHealthDto
                {
                    Component = "SigningProvider",
                    Status = "Unhealthy",
                    Description = $"Signing key registry failure: {ex.Message}"
                };
                report.OverallStatus = "Degraded";
            }

            // 3. Active Release Summary & Counts
            var releases = await _releaseRepository.GetByOrganizationIdAsync(targetOrgId, false, cancellationToken);
            report.TotalReleasesCount = releases.Count;

            var activeRelease = releases.FirstOrDefault(r => r.Status == UpdateReleaseStatus.Active);
            if (activeRelease != null)
            {
                var pkg = activeRelease.Packages.FirstOrDefault();
                report.ActiveRelease = new UpdateReleaseSummaryDto
                {
                    ReleaseId = activeRelease.Id,
                    Version = activeRelease.Version,
                    Status = activeRelease.Status.ToString(),
                    ReleaseType = activeRelease.ReleaseType.ToString(),
                    PublishedAt = activeRelease.PublishedAt,
                    PackageId = pkg?.Id,
                    PackageState = pkg?.LifecycleState.ToString(),
                    PackageSize = pkg?.Size ?? 0
                };
            }

            var targets = await _targetRepository.GetByOrganizationIdAsync(targetOrgId, false, cancellationToken);
            report.TotalTargetsCount = targets.Count;

            return Result<UpdateOperationalStatusDto>.Success(report);
        }
    }
}
