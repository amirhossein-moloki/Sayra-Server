using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Moq;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Security;
using Sayra.Backend.Application.Updates;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Exceptions;
using Sayra.Backend.Infrastructure.Persistence;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class UpdateEligibilityUnitTests
    {
        private static ApplicationDbContext CreateInMemoryDbContext(string dbName)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .Options;
            return new ApplicationDbContext(options);
        }

        private static UserPrincipal CreateAdminPrincipal(Guid orgId)
        {
            return new UserPrincipal
            {
                UserId = Guid.NewGuid(),
                Username = "admin_user",
                OrganizationId = orgId,
                Permissions = new List<string> { PermissionCatalog.ManageUpdates, PermissionCatalog.ViewUpdates },
                IsAuthenticated = true
            };
        }

        private static (UpdateRelease Release, UpdatePackage Package) CreateSignedReleaseAndPackage(
            Guid orgId,
            string version,
            UpdateReleaseType releaseType = UpdateReleaseType.Standard,
            UpdateReleaseStatus status = UpdateReleaseStatus.Active)
        {
            var release = UpdateRelease.Create(orgId, version, releaseType, "Notes", "admin");
            var package = UpdatePackage.Create(release.Id, $"app-{version}.spk", 1024, $"packages/{release.Id}/app.spk");

            package.TransitionLifecycle(UpdatePackageLifecycleState.Uploaded);
            package.TransitionLifecycle(UpdatePackageLifecycleState.Validating);
            package.TransitionLifecycle(UpdatePackageLifecycleState.Validated);
            package.SetIntegrity("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
            package.SignPackage("SampleSignatureBase64==", "key-1");
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

        #region 1. Deterministic Staged Rollout Bucket Tests

        [Fact]
        public void RolloutBucket_IdenticalInput_ProducesIdenticalBucket()
        {
            using var dbContext = CreateInMemoryDbContext(Guid.NewGuid().ToString());
            var service = new UpdateEligibilityService(
                new Repository<Workstation>(dbContext),
                new WorkstationGroupRepository(dbContext),
                new UpdateReleaseRepository(dbContext),
                new UpdateTargetRepository(dbContext));

            string pcId = "PC-NORTH-001";
            Guid releaseId = Guid.Parse("11111111-2222-3333-4444-555555555555");

            int bucket1 = service.CalculateRolloutBucket(pcId, releaseId);
            int bucket2 = service.CalculateRolloutBucket(pcId, releaseId);
            int bucket3 = service.CalculateRolloutBucket("pc-north-001 ", releaseId); // Case-insensitive and trimmed

            Assert.Equal(bucket1, bucket2);
            Assert.Equal(bucket1, bucket3);
            Assert.InRange(bucket1, 0, 99);
        }

        [Fact]
        public void RolloutBucket_MonotonicInclusion_AdmittedClientsRemainAdmittedAsPercentageIncreases()
        {
            using var dbContext = CreateInMemoryDbContext(Guid.NewGuid().ToString());
            var service = new UpdateEligibilityService(
                new Repository<Workstation>(dbContext),
                new WorkstationGroupRepository(dbContext),
                new UpdateReleaseRepository(dbContext),
                new UpdateTargetRepository(dbContext));

            Guid releaseId = Guid.NewGuid();
            int admittedAt10 = 0;
            int admittedAt25 = 0;
            int admittedAt50 = 0;
            int admittedAt100 = 0;

            for (int i = 0; i < 1000; i++)
            {
                string pcId = $"WORKSTATION-{i:D4}";
                int bucket = service.CalculateRolloutBucket(pcId, releaseId);

                bool in10 = bucket < 10;
                bool in25 = bucket < 25;
                bool in50 = bucket < 50;
                bool in100 = bucket < 100;

                if (in10)
                {
                    Assert.True(in25, "Client admitted at 10% must also be admitted at 25%");
                    Assert.True(in50, "Client admitted at 10% must also be admitted at 50%");
                    Assert.True(in100, "Client admitted at 10% must also be admitted at 100%");
                    admittedAt10++;
                }

                if (in25) admittedAt25++;
                if (in50) admittedAt50++;
                if (in100) admittedAt100++;
            }

            Assert.True(admittedAt10 < admittedAt25);
            Assert.True(admittedAt25 < admittedAt50);
            Assert.Equal(1000, admittedAt100);
        }

        #endregion

        #region 2. Scope Precedence Tests

        [Fact]
        public async Task Eligibility_ScopePrecedence_WorkstationTargetOverridesGroupSiteAndGlobal()
        {
            using var dbContext = CreateInMemoryDbContext(Guid.NewGuid().ToString());
            var releaseRepo = new UpdateReleaseRepository(dbContext);
            var targetRepo = new UpdateTargetRepository(dbContext);
            var workstationRepo = new Repository<Workstation>(dbContext);
            var groupRepo = new WorkstationGroupRepository(dbContext);

            var orgId = Guid.NewGuid();
            var siteId = Guid.NewGuid();

            // Create workstation
            var workstation = new Workstation
            {
                Name = "PC-01",
                PcId = "PC-01",
                SiteId = "SITE-1",
                OrganizationEntityId = orgId,
                SiteEntityId = siteId,
                Hostname = "host-01",
                IpAddress = "192.168.1.50",
                MacAddress = "AA:BB:CC:DD:EE:FF",
                ClientVersion = "1.0.0",
                Status = "OFFLINE"
            };

            // Global Release v1.1.0
            var (globalRelease, _) = CreateSignedReleaseAndPackage(orgId, "1.1.0");
            var globalTarget = UpdateTarget.CreateGlobal(orgId, globalRelease.Id, 100);

            // Workstation Specific Release v1.2.0
            var (wsRelease, _) = CreateSignedReleaseAndPackage(orgId, "1.2.0");
            var wsTarget = UpdateTarget.CreateWorkstation(orgId, wsRelease.Id, workstation.Id, siteId);

            await workstationRepo.AddAsync(workstation);
            await releaseRepo.AddAsync(globalRelease);
            await releaseRepo.AddAsync(wsRelease);
            await targetRepo.AddAsync(globalTarget);
            await targetRepo.AddAsync(wsTarget);
            await dbContext.SaveChangesAsync();

            var service = new UpdateEligibilityService(workstationRepo, groupRepo, releaseRepo, targetRepo);

            var result = await service.EvaluateWorkstationEligibilityAsync(workstation.Id, "1.0.0");

            Assert.True(result.IsEligible);
            Assert.Equal("1.2.0", result.TargetVersion);
            Assert.Equal(wsRelease.Id, result.SelectedReleaseId);
        }

        #endregion

        #region 3. Release Lifecycle & Revocation Protection Tests

        [Fact]
        public async Task Eligibility_RevokedRelease_IsExcludedFromEligibility()
        {
            using var dbContext = CreateInMemoryDbContext(Guid.NewGuid().ToString());
            var releaseRepo = new UpdateReleaseRepository(dbContext);
            var targetRepo = new UpdateTargetRepository(dbContext);
            var workstationRepo = new Repository<Workstation>(dbContext);
            var groupRepo = new WorkstationGroupRepository(dbContext);

            var orgId = Guid.NewGuid();
            var workstation = new Workstation
            {
                PcId = "PC-REVOKED-TEST",
                SiteId = "SITE-1",
                OrganizationEntityId = orgId,
                Hostname = "host-01",
                IpAddress = "10.0.0.1",
                MacAddress = "AA:BB:CC:DD:EE:01",
                ClientVersion = "1.0.0"
            };

            var (revokedRelease, _) = CreateSignedReleaseAndPackage(orgId, "1.1.0", status: UpdateReleaseStatus.Revoked);
            var target = UpdateTarget.CreateGlobal(orgId, revokedRelease.Id, 100);

            await workstationRepo.AddAsync(workstation);
            await releaseRepo.AddAsync(revokedRelease);
            await targetRepo.AddAsync(target);
            await dbContext.SaveChangesAsync();

            var service = new UpdateEligibilityService(workstationRepo, groupRepo, releaseRepo, targetRepo);

            var result = await service.EvaluateWorkstationEligibilityAsync(workstation.Id, "1.0.0");

            Assert.False(result.IsEligible);
            Assert.Equal(EligibilityReasonCodes.ReleaseRevoked, result.ReasonCode);
        }

        #endregion

        #region 4. Cryptographic Readiness Tests

        [Fact]
        public async Task Eligibility_UnsignedPackage_IsExcludedFromEligibility()
        {
            using var dbContext = CreateInMemoryDbContext(Guid.NewGuid().ToString());
            var releaseRepo = new UpdateReleaseRepository(dbContext);
            var targetRepo = new UpdateTargetRepository(dbContext);
            var workstationRepo = new Repository<Workstation>(dbContext);
            var groupRepo = new WorkstationGroupRepository(dbContext);

            var orgId = Guid.NewGuid();
            var workstation = new Workstation
            {
                PcId = "PC-UNSIGNED-TEST",
                SiteId = "SITE-1",
                OrganizationEntityId = orgId,
                Hostname = "host-01",
                IpAddress = "10.0.0.2",
                MacAddress = "AA:BB:CC:DD:EE:02",
                ClientVersion = "1.0.0"
            };

            var release = UpdateRelease.Create(orgId, "1.1.0");
            var unsignedPackage = UpdatePackage.Create(release.Id, "app.spk", 1024, "key.spk");
            // Unsigned package stays in Uploading state
            release.AddPackage(unsignedPackage);
            release.TransitionTo(UpdateReleaseStatus.Validated);
            release.TransitionTo(UpdateReleaseStatus.Ready);
            release.TransitionTo(UpdateReleaseStatus.Published);

            var target = UpdateTarget.CreateGlobal(orgId, release.Id, 100);

            await workstationRepo.AddAsync(workstation);
            await releaseRepo.AddAsync(release);
            await targetRepo.AddAsync(target);
            await dbContext.SaveChangesAsync();

            var service = new UpdateEligibilityService(workstationRepo, groupRepo, releaseRepo, targetRepo);

            var result = await service.EvaluateWorkstationEligibilityAsync(workstation.Id, "1.0.0");

            Assert.False(result.IsEligible);
            Assert.Equal(EligibilityReasonCodes.PackageNotReady, result.ReasonCode);
        }

        #endregion

        #region 5. Cross-Organization Isolation & Forgery Protection Tests

        [Fact]
        public async Task Eligibility_CrossOrganizationRelease_IsBlocked()
        {
            using var dbContext = CreateInMemoryDbContext(Guid.NewGuid().ToString());
            var releaseRepo = new UpdateReleaseRepository(dbContext);
            var targetRepo = new UpdateTargetRepository(dbContext);
            var workstationRepo = new Repository<Workstation>(dbContext);
            var groupRepo = new WorkstationGroupRepository(dbContext);

            var orgA = Guid.NewGuid();
            var orgB = Guid.NewGuid();

            var workstationOrgA = new Workstation
            {
                PcId = "PC-ORG-A",
                SiteId = "SITE-A",
                OrganizationEntityId = orgA,
                Hostname = "host-a",
                IpAddress = "10.0.0.10",
                MacAddress = "AA:BB:CC:DD:EE:10",
                ClientVersion = "1.0.0"
            };

            // Release belonging to Org B
            var (releaseOrgB, _) = CreateSignedReleaseAndPackage(orgB, "2.0.0");

            await workstationRepo.AddAsync(workstationOrgA);
            await releaseRepo.AddAsync(releaseOrgB);
            await dbContext.SaveChangesAsync();

            var service = new UpdateEligibilityService(workstationRepo, groupRepo, releaseRepo, targetRepo);

            // Attempt evaluation for Workstation A against Org B release
            var context = new ClientEvaluationContext
            {
                OrganizationId = orgA, // Workstation's real org
                WorkstationId = workstationOrgA.Id,
                PcId = workstationOrgA.PcId,
                CurrentVersion = "1.0.0"
            };

            var result = await service.EvaluateEligibilityAsync(context);

            Assert.False(result.IsEligible);
            Assert.NotEqual(releaseOrgB.Id, result.SelectedReleaseId);
        }

        #endregion

        #region 6. Version Rules, Downgrade & Rollback Policy Tests

        [Fact]
        public async Task Eligibility_ClientAlreadyCurrent_ReturnsClientAlreadyCurrentReason()
        {
            using var dbContext = CreateInMemoryDbContext(Guid.NewGuid().ToString());
            var releaseRepo = new UpdateReleaseRepository(dbContext);
            var targetRepo = new UpdateTargetRepository(dbContext);
            var workstationRepo = new Repository<Workstation>(dbContext);
            var groupRepo = new WorkstationGroupRepository(dbContext);

            var orgId = Guid.NewGuid();
            var workstation = new Workstation
            {
                PcId = "PC-CURRENT",
                SiteId = "SITE-1",
                OrganizationEntityId = orgId,
                Hostname = "host-01",
                IpAddress = "10.0.0.3",
                MacAddress = "AA:BB:CC:DD:EE:03",
                ClientVersion = "1.5.0"
            };

            var (release, _) = CreateSignedReleaseAndPackage(orgId, "1.5.0");
            var target = UpdateTarget.CreateGlobal(orgId, release.Id, 100);

            await workstationRepo.AddAsync(workstation);
            await releaseRepo.AddAsync(release);
            await targetRepo.AddAsync(target);
            await dbContext.SaveChangesAsync();

            var service = new UpdateEligibilityService(workstationRepo, groupRepo, releaseRepo, targetRepo);

            var result = await service.EvaluateWorkstationEligibilityAsync(workstation.Id, "1.5.0");

            Assert.False(result.IsEligible);
            Assert.Equal(EligibilityReasonCodes.ClientAlreadyCurrent, result.ReasonCode);
        }

        [Fact]
        public async Task Eligibility_UnauthorizedDowngrade_ReturnsRollbackNotAllowed()
        {
            using var dbContext = CreateInMemoryDbContext(Guid.NewGuid().ToString());
            var releaseRepo = new UpdateReleaseRepository(dbContext);
            var targetRepo = new UpdateTargetRepository(dbContext);
            var workstationRepo = new Repository<Workstation>(dbContext);
            var groupRepo = new WorkstationGroupRepository(dbContext);

            var orgId = Guid.NewGuid();
            var workstation = new Workstation
            {
                PcId = "PC-NEWER",
                SiteId = "SITE-1",
                OrganizationEntityId = orgId,
                Hostname = "host-01",
                IpAddress = "10.0.0.4",
                MacAddress = "AA:BB:CC:DD:EE:04",
                ClientVersion = "2.0.0"
            };

            // Standard release 1.5.0 (older than client 2.0.0)
            var (olderRelease, _) = CreateSignedReleaseAndPackage(orgId, "1.5.0", UpdateReleaseType.Standard);
            var target = UpdateTarget.CreateGlobal(orgId, olderRelease.Id, 100);

            await workstationRepo.AddAsync(workstation);
            await releaseRepo.AddAsync(olderRelease);
            await targetRepo.AddAsync(target);
            await dbContext.SaveChangesAsync();

            var service = new UpdateEligibilityService(workstationRepo, groupRepo, releaseRepo, targetRepo);

            var result = await service.EvaluateWorkstationEligibilityAsync(workstation.Id, "2.0.0");

            Assert.False(result.IsEligible);
            Assert.Equal(EligibilityReasonCodes.RollbackNotAllowed, result.ReasonCode);
        }

        [Fact]
        public async Task Eligibility_AuthorizedEmergencyRollback_PermitsDowngrade()
        {
            using var dbContext = CreateInMemoryDbContext(Guid.NewGuid().ToString());
            var releaseRepo = new UpdateReleaseRepository(dbContext);
            var targetRepo = new UpdateTargetRepository(dbContext);
            var workstationRepo = new Repository<Workstation>(dbContext);
            var groupRepo = new WorkstationGroupRepository(dbContext);

            var orgId = Guid.NewGuid();
            var workstation = new Workstation
            {
                PcId = "PC-ROLLBACK-TARGET",
                SiteId = "SITE-1",
                OrganizationEntityId = orgId,
                Hostname = "host-01",
                IpAddress = "10.0.0.5",
                MacAddress = "AA:BB:CC:DD:EE:05",
                ClientVersion = "2.0.0"
            };

            // Emergency release 1.5.0 (authorized rollback path)
            var (rollbackRelease, _) = CreateSignedReleaseAndPackage(orgId, "1.5.0", UpdateReleaseType.Emergency);
            var target = UpdateTarget.CreateGlobal(orgId, rollbackRelease.Id, 100);

            await workstationRepo.AddAsync(workstation);
            await releaseRepo.AddAsync(rollbackRelease);
            await targetRepo.AddAsync(target);
            await dbContext.SaveChangesAsync();

            var service = new UpdateEligibilityService(workstationRepo, groupRepo, releaseRepo, targetRepo);

            var result = await service.EvaluateWorkstationEligibilityAsync(workstation.Id, "2.0.0");

            Assert.True(result.IsEligible);
            Assert.Equal("1.5.0", result.TargetVersion);
            Assert.True(result.IsMandatory);
        }

        #endregion

        #region 7. Minimum Version Enforcement & Mandatory Override Tests

        [Fact]
        public async Task Eligibility_ClientBelowMinimumSupportedVersion_EnforcesMandatoryUpdate()
        {
            using var dbContext = CreateInMemoryDbContext(Guid.NewGuid().ToString());
            var releaseRepo = new UpdateReleaseRepository(dbContext);
            var targetRepo = new UpdateTargetRepository(dbContext);
            var workstationRepo = new Repository<Workstation>(dbContext);
            var groupRepo = new WorkstationGroupRepository(dbContext);

            var orgId = Guid.NewGuid();
            var workstation = new Workstation
            {
                PcId = "PC-OUTDATED",
                SiteId = "SITE-1",
                OrganizationEntityId = orgId,
                Hostname = "host-01",
                IpAddress = "10.0.0.6",
                MacAddress = "AA:BB:CC:DD:EE:06",
                ClientVersion = "0.9.0" // Outdated
            };

            var (release, _) = CreateSignedReleaseAndPackage(orgId, "1.5.0");
            // Target sets MinimumSupportedVersion = 1.0.0 with 0% normal rollout
            var target = UpdateTarget.CreateGlobal(orgId, release.Id, rolloutPercentage: 0, minimumSupportedVersion: "1.0.0");

            await workstationRepo.AddAsync(workstation);
            await releaseRepo.AddAsync(release);
            await targetRepo.AddAsync(target);
            await dbContext.SaveChangesAsync();

            var service = new UpdateEligibilityService(workstationRepo, groupRepo, releaseRepo, targetRepo);

            var result = await service.EvaluateWorkstationEligibilityAsync(workstation.Id, "0.9.0");

            Assert.True(result.IsEligible);
            Assert.True(result.IsMandatory, "Clients below minimum supported version must receive mandatory update bypass");
            Assert.Equal("1.5.0", result.TargetVersion);
        }

        #endregion

        #region 8. Administrative CQRS Command Handlers Tests

        [Fact]
        public async Task CreateUpdateTargetHandler_ValidParameters_CreatesTargetAndAudit()
        {
            using var dbContext = CreateInMemoryDbContext(Guid.NewGuid().ToString());
            var targetRepo = new UpdateTargetRepository(dbContext);
            var releaseRepo = new UpdateReleaseRepository(dbContext);
            var orgRepo = new Repository<Organization>(dbContext);
            var siteRepo = new Repository<Site>(dbContext);
            var groupRepo = new WorkstationGroupRepository(dbContext);
            var wsRepo = new Repository<Workstation>(dbContext);
            var authMock = new Mock<IAuthorizationService>();
            var auditMock = new Mock<ISecurityEventService>();

            var orgId = Guid.NewGuid();
            var (release, _) = CreateSignedReleaseAndPackage(orgId, "1.0.0");
            await releaseRepo.AddAsync(release);
            await dbContext.SaveChangesAsync();

            authMock.Setup(a => a.AuthorizeAsync(It.IsAny<UserPrincipal>(), It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(AuthorizationResult.Allowed());

            var admin = CreateAdminPrincipal(orgId);
            var handler = new CreateUpdateTargetCommandHandler(
                targetRepo, releaseRepo, orgRepo, siteRepo, groupRepo, wsRepo, dbContext, authMock.Object, auditMock.Object);

            var command = new CreateUpdateTargetCommand
            {
                OrganizationId = orgId,
                ReleaseId = release.Id,
                TargetType = ConfigurationTargetType.Global,
                RolloutPercentage = 50,
                MinimumSupportedVersion = "0.8.0",
                Principal = admin
            };

            var result = await handler.HandleAsync(command);

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal(50, result.Value.RolloutPercentage);
            Assert.Equal("Global", result.Value.TargetType);

            auditMock.Verify(a => a.RecordSecurityEventAsync(
                "UPDATE_TARGET_CREATED",
                admin.UserId,
                "User",
                null,
                orgId,
                null,
                "UpdateTarget",
                result.Value.TargetId,
                "CREATE_UPDATE_TARGET",
                "SUCCESS",
                null,
                null,
                null,
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task UpdateRolloutPercentageHandler_ValidChange_UpdatesPercentageAndEmitsAudit()
        {
            using var dbContext = CreateInMemoryDbContext(Guid.NewGuid().ToString());
            var targetRepo = new UpdateTargetRepository(dbContext);
            var releaseRepo = new UpdateReleaseRepository(dbContext);
            var authMock = new Mock<IAuthorizationService>();
            var auditMock = new Mock<ISecurityEventService>();

            var orgId = Guid.NewGuid();
            var (release, _) = CreateSignedReleaseAndPackage(orgId, "1.0.0");
            var target = UpdateTarget.CreateGlobal(orgId, release.Id, 10);

            await releaseRepo.AddAsync(release);
            await targetRepo.AddAsync(target);
            await dbContext.SaveChangesAsync();

            authMock.Setup(a => a.AuthorizeAsync(It.IsAny<UserPrincipal>(), It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(AuthorizationResult.Allowed());

            var admin = CreateAdminPrincipal(orgId);
            var handler = new UpdateRolloutPercentageCommandHandler(targetRepo, dbContext, authMock.Object, auditMock.Object);

            var command = new UpdateRolloutPercentageCommand
            {
                TargetId = target.Id,
                RolloutPercentage = 100,
                Principal = admin
            };

            var result = await handler.HandleAsync(command);

            Assert.True(result.IsSuccess);
            Assert.Equal(100, result.Value!.RolloutPercentage);

            auditMock.Verify(a => a.RecordSecurityEventAsync(
                "UPDATE_ROLLOUT_COMPLETED",
                admin.UserId,
                "User",
                null,
                orgId,
                null,
                "UpdateTarget",
                target.Id,
                "UPDATE_ROLLOUT",
                "SUCCESS",
                It.IsAny<string>(),
                null,
                null,
                It.IsAny<CancellationToken>()), Times.Once);
        }

        #endregion
    }
}
