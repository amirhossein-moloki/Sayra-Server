using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Security;
using Sayra.Backend.Application.Updates;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Infrastructure.Persistence;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class UpdateManifestUnitTests
    {
        private static ApplicationDbContext CreateInMemoryDbContext(string dbName)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .Options;
            return new ApplicationDbContext(options);
        }

        private static UserPrincipal CreateWorkstationPrincipal(Guid orgId, string pcId, Guid? userId = null)
        {
            return new UserPrincipal
            {
                UserId = userId ?? Guid.NewGuid(),
                Username = $"workstation_{pcId.ToLowerInvariant()}",
                PcId = pcId,
                OrganizationId = orgId,
                Permissions = new List<string> { PermissionCatalog.ViewUpdates },
                IsAuthenticated = true
            };
        }

        private static (UpdateRelease Release, UpdatePackage Package) CreateSignedReleaseAndPackage(
            Guid orgId,
            string version,
            UpdateReleaseType releaseType = UpdateReleaseType.Standard,
            UpdateReleaseStatus status = UpdateReleaseStatus.Active)
        {
            var release = UpdateRelease.Create(orgId, version, releaseType, $"Release notes for {version}", "admin");
            var package = UpdatePackage.Create(release.Id, $"sayra-client-{version}.spk", 10485760, $"packages/{release.Id}/app.spk");

            package.TransitionLifecycle(UpdatePackageLifecycleState.Uploaded);
            package.TransitionLifecycle(UpdatePackageLifecycleState.Validating);
            package.TransitionLifecycle(UpdatePackageLifecycleState.Validated);
            package.SetIntegrity("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
            package.SignPackage("ValidRSASignatureBase64String==", "key-1");
            package.TransitionLifecycle(UpdatePackageLifecycleState.Ready);

            release.AddPackage(package);

            if (status == UpdateReleaseStatus.Validated)
            {
                release.TransitionTo(UpdateReleaseStatus.Validated);
            }
            else if (status == UpdateReleaseStatus.Ready)
            {
                release.TransitionTo(UpdateReleaseStatus.Validated);
                release.TransitionTo(UpdateReleaseStatus.Ready);
            }
            else if (status == UpdateReleaseStatus.Published)
            {
                release.TransitionTo(UpdateReleaseStatus.Validated);
                release.TransitionTo(UpdateReleaseStatus.Ready);
                release.TransitionTo(UpdateReleaseStatus.Published);
            }
            else if (status == UpdateReleaseStatus.Active)
            {
                release.TransitionTo(UpdateReleaseStatus.Validated);
                release.TransitionTo(UpdateReleaseStatus.Ready);
                release.TransitionTo(UpdateReleaseStatus.Published);
                release.TransitionTo(UpdateReleaseStatus.Active);
            }
            else if (status == UpdateReleaseStatus.Revoked)
            {
                release.TransitionTo(UpdateReleaseStatus.Validated);
                release.TransitionTo(UpdateReleaseStatus.Ready);
                release.TransitionTo(UpdateReleaseStatus.Published);
                release.TransitionTo(UpdateReleaseStatus.Revoked);
            }

            return (release, package);
        }

        #region 1. Stage 07-02 Client Golden Contract Serialization Test

        [Fact]
        public void ManifestContract_GoldenSerialization_MatchesStage0702ContractSpecs()
        {
            var manifest = new ClientUpdateManifestContract
            {
                Version = "2.1.0",
                ReleaseNotes = "Golden contract test notes",
                PackageUrl = "https://updates.sayra.io/api/updates/download/00000000-0000-0000-0000-000000000001",
                Checksum = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                Signature = "MEYCIQ...==",
                IsMandatory = true,
                MinimumSupportedVersion = "1.0.0",
                FileSize = 5242880,
                PackageType = "spk"
            };

            string serializedJson = ProtocolSerialization.Serialize(manifest);

            Assert.Contains("\"version\":\"2.1.0\"", serializedJson);
            Assert.Contains("\"releaseNotes\":\"Golden contract test notes\"", serializedJson);
            Assert.Contains("\"packageUrl\":\"https://updates.sayra.io/api/updates/download/00000000-0000-0000-0000-000000000001\"", serializedJson);
            Assert.Contains("\"checksum\":\"e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855\"", serializedJson);
            Assert.Contains("\"signature\":\"MEYCIQ...==\"", serializedJson);
            Assert.Contains("\"isMandatory\":true", serializedJson);
            Assert.Contains("\"fileSize\":5242880", serializedJson);
            Assert.Contains("\"packageType\":\"spk\"", serializedJson);

            // Deserialization roundtrip
            var roundtrip = ProtocolSerialization.Deserialize<ClientUpdateManifestContract>(serializedJson);
            Assert.NotNull(roundtrip);
            Assert.Equal(manifest.Version, roundtrip.Version);
            Assert.Equal(manifest.PackageUrl, roundtrip.PackageUrl);
            Assert.Equal(manifest.Checksum, roundtrip.Checksum);
            Assert.Equal(manifest.Signature, roundtrip.Signature);
            Assert.True(roundtrip.IsMandatory);
        }

        #endregion

        #region 2. Happy Path Manifest Discovery

        [Fact]
        public async Task GetManifest_EligibleWorkstation_ReturnsValidManifestContract()
        {
            using var dbContext = CreateInMemoryDbContext(Guid.NewGuid().ToString());
            var releaseRepo = new UpdateReleaseRepository(dbContext);
            var targetRepo = new UpdateTargetRepository(dbContext);
            var wsRepo = new Repository<Workstation>(dbContext);
            var groupRepo = new WorkstationGroupRepository(dbContext);

            var orgId = Guid.NewGuid();
            var workstation = new Workstation
            {
                PcId = "PC-MANIFEST-001",
                SiteId = "SITE-1",
                OrganizationEntityId = orgId,
                Hostname = "host-01",
                IpAddress = "10.0.0.10",
                MacAddress = "AA:BB:CC:DD:EE:10",
                ClientVersion = "1.0.0"
            };

            var (release, package) = CreateSignedReleaseAndPackage(orgId, "1.1.0");
            var target = UpdateTarget.CreateGlobal(orgId, release.Id, 100);

            await wsRepo.AddAsync(workstation);
            await releaseRepo.AddAsync(release);
            await targetRepo.AddAsync(target);
            await dbContext.SaveChangesAsync();

            var eligibilityService = new UpdateEligibilityService(wsRepo, groupRepo, releaseRepo, targetRepo);
            var manifestService = new UpdateManifestService(eligibilityService, wsRepo, NullLogger<UpdateManifestService>.Instance);

            var principal = CreateWorkstationPrincipal(orgId, workstation.PcId, workstation.Id);
            var request = new UpdateManifestRequest
            {
                Principal = principal,
                ReportedVersion = "1.0.0",
                DownloadBaseUrl = "https://api.sayra.io"
            };

            var result = await manifestService.GetManifestAsync(request);

            Assert.True(result.UpdateAvailable);
            Assert.NotNull(result.Manifest);
            Assert.Equal("1.1.0", result.Manifest.Version);
            Assert.Equal($"https://api.sayra.io/api/updates/download/{package.Id}", result.Manifest.PackageUrl);
            Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", result.Manifest.Checksum);
            Assert.Equal("ValidRSASignatureBase64String==", result.Manifest.Signature);
            Assert.Equal(10485760, result.Manifest.FileSize);
            Assert.Equal("spk", result.Manifest.PackageType);
        }

        #endregion

        #region 3. Update Not Available & Staged Rollout Exclusion Tests

        [Fact]
        public async Task GetManifest_ClientAlreadyUpToDate_ReturnsNoUpdateAvailable()
        {
            using var dbContext = CreateInMemoryDbContext(Guid.NewGuid().ToString());
            var releaseRepo = new UpdateReleaseRepository(dbContext);
            var targetRepo = new UpdateTargetRepository(dbContext);
            var wsRepo = new Repository<Workstation>(dbContext);
            var groupRepo = new WorkstationGroupRepository(dbContext);

            var orgId = Guid.NewGuid();
            var workstation = new Workstation
            {
                PcId = "PC-CURRENT-001",
                SiteId = "SITE-1",
                OrganizationEntityId = orgId,
                Hostname = "host-01",
                IpAddress = "10.0.0.11",
                MacAddress = "AA:BB:CC:DD:EE:11",
                ClientVersion = "1.1.0"
            };

            var (release, _) = CreateSignedReleaseAndPackage(orgId, "1.1.0");
            var target = UpdateTarget.CreateGlobal(orgId, release.Id, 100);

            await wsRepo.AddAsync(workstation);
            await releaseRepo.AddAsync(release);
            await targetRepo.AddAsync(target);
            await dbContext.SaveChangesAsync();

            var eligibilityService = new UpdateEligibilityService(wsRepo, groupRepo, releaseRepo, targetRepo);
            var manifestService = new UpdateManifestService(eligibilityService, wsRepo, NullLogger<UpdateManifestService>.Instance);

            var principal = CreateWorkstationPrincipal(orgId, workstation.PcId, workstation.Id);
            var request = new UpdateManifestRequest
            {
                Principal = principal,
                ReportedVersion = "1.1.0"
            };

            var result = await manifestService.GetManifestAsync(request);

            Assert.False(result.UpdateAvailable);
            Assert.Null(result.Manifest);
            Assert.Equal(EligibilityReasonCodes.ClientAlreadyCurrent, result.ReasonCode);
        }

        [Fact]
        public async Task GetManifest_OutsideRolloutPercentage_ReturnsNoUpdateAvailable()
        {
            using var dbContext = CreateInMemoryDbContext(Guid.NewGuid().ToString());
            var releaseRepo = new UpdateReleaseRepository(dbContext);
            var targetRepo = new UpdateTargetRepository(dbContext);
            var wsRepo = new Repository<Workstation>(dbContext);
            var groupRepo = new WorkstationGroupRepository(dbContext);

            var orgId = Guid.NewGuid();
            var workstation = new Workstation
            {
                PcId = "PC-ROLLOUT-EXCLUDED",
                SiteId = "SITE-1",
                OrganizationEntityId = orgId,
                Hostname = "host-01",
                IpAddress = "10.0.0.12",
                MacAddress = "AA:BB:CC:DD:EE:12",
                ClientVersion = "1.0.0"
            };

            var (release, _) = CreateSignedReleaseAndPackage(orgId, "1.2.0");
            // Set 0% rollout percentage
            var target = UpdateTarget.CreateGlobal(orgId, release.Id, rolloutPercentage: 0);

            await wsRepo.AddAsync(workstation);
            await releaseRepo.AddAsync(release);
            await targetRepo.AddAsync(target);
            await dbContext.SaveChangesAsync();

            var eligibilityService = new UpdateEligibilityService(wsRepo, groupRepo, releaseRepo, targetRepo);
            var manifestService = new UpdateManifestService(eligibilityService, wsRepo, NullLogger<UpdateManifestService>.Instance);

            var principal = CreateWorkstationPrincipal(orgId, workstation.PcId, workstation.Id);
            var request = new UpdateManifestRequest
            {
                Principal = principal,
                ReportedVersion = "1.0.0"
            };

            var result = await manifestService.GetManifestAsync(request);

            Assert.False(result.UpdateAvailable);
            Assert.Null(result.Manifest);
            Assert.Equal(EligibilityReasonCodes.RolloutNotEligible, result.ReasonCode);
        }

        #endregion

        #region 4. Mandatory & Security Release Handling

        [Fact]
        public async Task GetManifest_SecurityRelease_IsFlaggedMandatoryInContract()
        {
            using var dbContext = CreateInMemoryDbContext(Guid.NewGuid().ToString());
            var releaseRepo = new UpdateReleaseRepository(dbContext);
            var targetRepo = new UpdateTargetRepository(dbContext);
            var wsRepo = new Repository<Workstation>(dbContext);
            var groupRepo = new WorkstationGroupRepository(dbContext);

            var orgId = Guid.NewGuid();
            var workstation = new Workstation
            {
                PcId = "PC-SEC-001",
                SiteId = "SITE-1",
                OrganizationEntityId = orgId,
                Hostname = "host-01",
                IpAddress = "10.0.0.13",
                MacAddress = "AA:BB:CC:DD:EE:13",
                ClientVersion = "1.0.0"
            };

            var (securityRelease, _) = CreateSignedReleaseAndPackage(orgId, "1.1.0", releaseType: UpdateReleaseType.Security);
            var target = UpdateTarget.CreateGlobal(orgId, securityRelease.Id, 10);

            await wsRepo.AddAsync(workstation);
            await releaseRepo.AddAsync(securityRelease);
            await targetRepo.AddAsync(target);
            await dbContext.SaveChangesAsync();

            var eligibilityService = new UpdateEligibilityService(wsRepo, groupRepo, releaseRepo, targetRepo);
            var manifestService = new UpdateManifestService(eligibilityService, wsRepo, NullLogger<UpdateManifestService>.Instance);

            var principal = CreateWorkstationPrincipal(orgId, workstation.PcId, workstation.Id);
            var request = new UpdateManifestRequest
            {
                Principal = principal,
                ReportedVersion = "1.0.0"
            };

            var result = await manifestService.GetManifestAsync(request);

            Assert.True(result.UpdateAvailable);
            Assert.NotNull(result.Manifest);
            Assert.True(result.Manifest.IsMandatory, "Security releases must be marked as mandatory");
        }

        #endregion

        #region 5. Revoked Release & Invalid Package Safety

        [Fact]
        public async Task GetManifest_RevokedRelease_IsNeverAdvertised()
        {
            using var dbContext = CreateInMemoryDbContext(Guid.NewGuid().ToString());
            var releaseRepo = new UpdateReleaseRepository(dbContext);
            var targetRepo = new UpdateTargetRepository(dbContext);
            var wsRepo = new Repository<Workstation>(dbContext);
            var groupRepo = new WorkstationGroupRepository(dbContext);

            var orgId = Guid.NewGuid();
            var workstation = new Workstation
            {
                PcId = "PC-REVOKED-TEST",
                SiteId = "SITE-1",
                OrganizationEntityId = orgId,
                Hostname = "host-01",
                IpAddress = "10.0.0.14",
                MacAddress = "AA:BB:CC:DD:EE:14",
                ClientVersion = "1.0.0"
            };

            var (revokedRelease, _) = CreateSignedReleaseAndPackage(orgId, "1.2.0", status: UpdateReleaseStatus.Revoked);
            var target = UpdateTarget.CreateGlobal(orgId, revokedRelease.Id, 100);

            await wsRepo.AddAsync(workstation);
            await releaseRepo.AddAsync(revokedRelease);
            await targetRepo.AddAsync(target);
            await dbContext.SaveChangesAsync();

            var eligibilityService = new UpdateEligibilityService(wsRepo, groupRepo, releaseRepo, targetRepo);
            var manifestService = new UpdateManifestService(eligibilityService, wsRepo, NullLogger<UpdateManifestService>.Instance);

            var principal = CreateWorkstationPrincipal(orgId, workstation.PcId, workstation.Id);
            var request = new UpdateManifestRequest
            {
                Principal = principal,
                ReportedVersion = "1.0.0"
            };

            var result = await manifestService.GetManifestAsync(request);

            Assert.False(result.UpdateAvailable);
            Assert.Null(result.Manifest);
            Assert.Equal(EligibilityReasonCodes.ReleaseRevoked, result.ReasonCode);
        }

        #endregion

        #region 6. Organization Isolation & Tenant Safety

        [Fact]
        public async Task GetManifest_CrossOrganizationRequest_IsStrictlyDenied()
        {
            using var dbContext = CreateInMemoryDbContext(Guid.NewGuid().ToString());
            var releaseRepo = new UpdateReleaseRepository(dbContext);
            var targetRepo = new UpdateTargetRepository(dbContext);
            var wsRepo = new Repository<Workstation>(dbContext);
            var groupRepo = new WorkstationGroupRepository(dbContext);

            var orgA = Guid.NewGuid();
            var orgB = Guid.NewGuid();

            var workstationOrgA = new Workstation
            {
                PcId = "PC-ORG-A-ISOLATION",
                SiteId = "SITE-A",
                OrganizationEntityId = orgA,
                Hostname = "host-a",
                IpAddress = "10.0.0.15",
                MacAddress = "AA:BB:CC:DD:EE:15",
                ClientVersion = "1.0.0"
            };

            // Release belonging to Org B
            var (releaseOrgB, _) = CreateSignedReleaseAndPackage(orgB, "2.0.0");
            var targetOrgB = UpdateTarget.CreateGlobal(orgB, releaseOrgB.Id, 100);

            await wsRepo.AddAsync(workstationOrgA);
            await releaseRepo.AddAsync(releaseOrgB);
            await targetRepo.AddAsync(targetOrgB);
            await dbContext.SaveChangesAsync();

            var eligibilityService = new UpdateEligibilityService(wsRepo, groupRepo, releaseRepo, targetRepo);
            var manifestService = new UpdateManifestService(eligibilityService, wsRepo, NullLogger<UpdateManifestService>.Instance);

            // Principal presenting Org B identity while workstation belongs to Org A
            var maliciousPrincipal = CreateWorkstationPrincipal(orgB, workstationOrgA.PcId, workstationOrgA.Id);
            var request = new UpdateManifestRequest
            {
                Principal = maliciousPrincipal,
                ReportedVersion = "1.0.0"
            };

            var result = await manifestService.GetManifestAsync(request);

            Assert.False(result.UpdateAvailable);
            Assert.Null(result.Manifest);
            Assert.Equal(EligibilityReasonCodes.OrganizationMismatch, result.ReasonCode);
        }

        #endregion

        #region 7. Security Audit Logging Verification

        [Fact]
        public async Task GetManifest_SuccessfulDiscovery_EmitsSecurityAuditEvent()
        {
            using var dbContext = CreateInMemoryDbContext(Guid.NewGuid().ToString());
            var releaseRepo = new UpdateReleaseRepository(dbContext);
            var targetRepo = new UpdateTargetRepository(dbContext);
            var wsRepo = new Repository<Workstation>(dbContext);
            var groupRepo = new WorkstationGroupRepository(dbContext);
            var auditMock = new Mock<ISecurityEventService>();

            var orgId = Guid.NewGuid();
            var workstation = new Workstation
            {
                PcId = "PC-AUDIT-TEST",
                SiteId = "SITE-1",
                OrganizationEntityId = orgId,
                Hostname = "host-01",
                IpAddress = "10.0.0.16",
                MacAddress = "AA:BB:CC:DD:EE:16",
                ClientVersion = "1.0.0"
            };

            var (release, _) = CreateSignedReleaseAndPackage(orgId, "1.1.0");
            var target = UpdateTarget.CreateGlobal(orgId, release.Id, 100);

            await wsRepo.AddAsync(workstation);
            await releaseRepo.AddAsync(release);
            await targetRepo.AddAsync(target);
            await dbContext.SaveChangesAsync();

            var eligibilityService = new UpdateEligibilityService(wsRepo, groupRepo, releaseRepo, targetRepo);
            var manifestService = new UpdateManifestService(eligibilityService, wsRepo, NullLogger<UpdateManifestService>.Instance, auditMock.Object);

            var principal = CreateWorkstationPrincipal(orgId, workstation.PcId, workstation.Id);
            var request = new UpdateManifestRequest
            {
                Principal = principal,
                ReportedVersion = "1.0.0"
            };

            var result = await manifestService.GetManifestAsync(request);

            Assert.True(result.UpdateAvailable);

            auditMock.Verify(a => a.RecordSecurityEventAsync(
                "UPDATE_MANIFEST_DISCOVERED",
                principal.UserId,
                "USER",
                workstation.PcId,
                orgId,
                null,
                "UpdateRelease",
                release.Id,
                "DISCOVER",
                "SUCCESS",
                null,
                null,
                null,
                It.IsAny<CancellationToken>()), Times.Once);
        }

        #endregion
    }
}
