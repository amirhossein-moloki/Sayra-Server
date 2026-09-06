using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Security;
using Sayra.Backend.Application.Updates;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Infrastructure.Configuration.Options;
using Sayra.Backend.Infrastructure.Persistence;
using Sayra.Backend.Infrastructure.Security;
using Sayra.Backend.Infrastructure.Updates;
using Xunit;

namespace Sayra.Backend.UnitTests.Updates
{
    public class Phase07TargetingAndIsolationE2ETests
    {
        private static ApplicationDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: $"E2E_Targeting_{Guid.NewGuid():N}")
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            return new ApplicationDbContext(options);
        }

        private static LocalUpdateArtifactStorage CreateTestStorage(string rootPath)
        {
            var options = Options.Create(new UpdatesOptions
            {
                LocalUpdateRepositoryPath = Path.Combine(rootPath, "packages"),
                TempRepositoryPath = Path.Combine(rootPath, "temp"),
                MaxArtifactSizeBytes = 100 * 1024 * 1024
            });

            return new LocalUpdateArtifactStorage(options, NullLogger<LocalUpdateArtifactStorage>.Instance);
        }

        private static byte[] CreateZipContent(string innerFileName, string innerContent)
        {
            using var ms = new MemoryStream();
            using (var archive = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true))
            {
                var entry = archive.CreateEntry(innerFileName);
                using var entryStream = entry.Open();
                byte[] contentBytes = Encoding.UTF8.GetBytes(innerContent);
                entryStream.Write(contentBytes, 0, contentBytes.Length);
            }
            return ms.ToArray();
        }

        private async Task<(UpdateRelease Release, UpdatePackage Package)> CreatePublishedReleaseAsync(
            ApplicationDbContext dbContext,
            Guid orgId,
            string version,
            LocalUpdateArtifactStorage storage,
            UpdateSigningService signingService,
            string keyId)
        {
            var releaseRepo = new UpdateReleaseRepository(dbContext);
            var packageRepo = new UpdatePackageRepository(dbContext);
            var hashService = new UpdateHashService();

            var release = UpdateRelease.Create(orgId, version, UpdateReleaseType.Standard, $"Release {version}", "admin");
            await releaseRepo.AddAsync(release);
            await dbContext.SaveChangesAsync();

            byte[] content = CreateZipContent("client.exe", $"CONTENT_{version}");
            using var ms = new MemoryStream(content);

            string tempKey = await storage.SaveTemporaryArtifactAsync(Guid.NewGuid(), ms, CancellationToken.None);
            ms.Position = 0;
            string sha256 = await hashService.ComputeSha256Async(ms, CancellationToken.None);
            long size = await storage.GetArtifactSizeAsync(tempKey, CancellationToken.None);

            var package = UpdatePackage.Create(release.Id, $"client-{version}.spk", size, tempKey, UpdatePackageType.Spk);
            package.TransitionLifecycle(UpdatePackageLifecycleState.Uploaded);
            package.TransitionLifecycle(UpdatePackageLifecycleState.Validating);
            package.TransitionLifecycle(UpdatePackageLifecycleState.Validated);
            package.SetIntegrity(sha256);

            string finalKey = $"packages/{release.Id:N}/{package.Id:N}.spk";
            await storage.FinalizeArtifactAsync(tempKey, finalKey, CancellationToken.None);
            package.UpdateStorageKeyAndSize(finalKey, size);

            var signRes = await signingService.SignPackageAsync(package, keyId, CancellationToken.None);
            package.SignPackage(signRes.Signature, signRes.KeyId);
            package.TransitionLifecycle(UpdatePackageLifecycleState.Ready);

            release.AddPackage(package);
            release.TransitionTo(UpdateReleaseStatus.Validated);
            release.TransitionTo(UpdateReleaseStatus.Ready);
            release.TransitionTo(UpdateReleaseStatus.Published);

            await packageRepo.AddAsync(package);
            await dbContext.SaveChangesAsync();

            return (release, package);
        }

        [Fact]
        public async Task ScopePrecedence_WorkstationOverGroupOverSiteOverGlobal_EvaluatesCorrectly()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"sayra_precedence_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            try
            {
                using var dbContext = CreateDbContext();

                var releaseRepo = new UpdateReleaseRepository(dbContext);
                var packageRepo = new UpdatePackageRepository(dbContext);
                var targetRepo = new UpdateTargetRepository(dbContext);
                var keyRegistryRepo = new ConfigurationKeyRegistryRepository(dbContext);
                var wsRepo = new Repository<Workstation>(dbContext);
                var orgRepo = new Repository<Organization>(dbContext);
                var siteRepo = new Repository<Site>(dbContext);
                var groupRepo = new WorkstationGroupRepository(dbContext);
                var secEventRepo = new Repository<SecurityEvent>(dbContext);

                var storage = CreateTestStorage(tempDir);
                var cryptoService = new CryptographicService();

                var privateKeyProvider = new SigningPrivateKeyProvider(Options.Create(new SecurityOptions()));
                var (keyId, pubPem, privPem) = SigningPrivateKeyProvider.GetOrCreateEphemeralKeyPair("key-prec-01");
                privateKeyProvider.RegisterTestKeyPair(keyId, pubPem, privPem);
                await keyRegistryRepo.AddAsync(ConfigurationSigningKey.Create(keyId, pubPem, "RSA-SHA256", SigningKeyStatus.Active));
                await dbContext.SaveChangesAsync();

                var updateSigningKeyProvider = new UpdateSigningKeyProvider(keyRegistryRepo, privateKeyProvider);
                var signingService = new UpdateSigningService(updateSigningKeyProvider, cryptoService);
                var eligibilityService = new UpdateEligibilityService(wsRepo, groupRepo, releaseRepo, targetRepo);

                var org = new Organization { Name = "Precedence Org", Code = "PRECORG", Status = "Active" };
                await orgRepo.AddAsync(org);

                var site = new Site { OrganizationId = org.Id, Name = "Site A", Code = "SITEA", Status = "Active" };
                await siteRepo.AddAsync(site);

                var group = new WorkstationGroup { OrganizationId = org.Id, Name = "Vip Group", Code = "VIP" };
                await groupRepo.AddAsync(group);

                var ws = new Workstation
                {
                    PcId = "PC-PREC-01",
                    Hostname = "pc-prec-01",
                    IpAddress = "192.168.1.10",
                    MacAddress = "00:11:22:33:44:55",
                    OrganizationEntityId = org.Id,
                    SiteEntityId = site.Id,
                    Status = "Available",
                    ClientVersion = "v1.0.0"
                };
                await wsRepo.AddAsync(ws);
                await dbContext.SaveChangesAsync();

                var groupMember = new WorkstationGroupMember { WorkstationGroupId = group.Id, WorkstationId = ws.Id };
                await groupRepo.AddMemberAsync(groupMember);
                await dbContext.SaveChangesAsync();

                // Create Releases: Global (v2.0.0), Site (v2.1.0), Group (v2.2.0), Workstation (v2.3.0)
                var (relGlobal, _) = await CreatePublishedReleaseAsync(dbContext, org.Id, "v2.0.0", storage, signingService, keyId);
                var (relSite, _) = await CreatePublishedReleaseAsync(dbContext, org.Id, "v2.1.0", storage, signingService, keyId);
                var (relGroup, _) = await CreatePublishedReleaseAsync(dbContext, org.Id, "v2.2.0", storage, signingService, keyId);
                var (relWs, _) = await CreatePublishedReleaseAsync(dbContext, org.Id, "v2.3.0", storage, signingService, keyId);

                // Add Targets
                await targetRepo.AddAsync(UpdateTarget.CreateGlobal(org.Id, relGlobal.Id, 100));
                await targetRepo.AddAsync(UpdateTarget.CreateSite(org.Id, relSite.Id, site.Id, 100));
                await targetRepo.AddAsync(UpdateTarget.CreateGroup(org.Id, relGroup.Id, group.Id, site.Id, 100));
                await targetRepo.AddAsync(UpdateTarget.CreateWorkstation(org.Id, relWs.Id, ws.Id, site.Id, group.Id, 100));
                await dbContext.SaveChangesAsync();

                // Workstation Target has highest precedence -> Should receive v2.3.0
                var eval1 = await eligibilityService.EvaluateWorkstationEligibilityAsync(ws.Id, "v1.0.0");
                Assert.True(eval1.IsEligible);
                Assert.Equal("v2.3.0", eval1.ReleaseContract!.Version);

                // Disable Workstation Target -> Group Target takes precedence -> Should receive v2.2.0
                var wsTarget = (await targetRepo.GetByReleaseIdAsync(relWs.Id, track: true)).First();
                wsTarget.Disable();
                await dbContext.SaveChangesAsync();

                var eval2 = await eligibilityService.EvaluateWorkstationEligibilityAsync(ws.Id, "v1.0.0");
                Assert.True(eval2.IsEligible);
                Assert.Equal("v2.2.0", eval2.ReleaseContract!.Version);

                // Disable Group Target -> Site Target takes precedence -> Should receive v2.1.0
                var groupTarget = (await targetRepo.GetByReleaseIdAsync(relGroup.Id, track: true)).First();
                groupTarget.Disable();
                await dbContext.SaveChangesAsync();

                var eval3 = await eligibilityService.EvaluateWorkstationEligibilityAsync(ws.Id, "v1.0.0");
                Assert.True(eval3.IsEligible);
                Assert.Equal("v2.1.0", eval3.ReleaseContract!.Version);

                // Disable Site Target -> Global Target takes precedence -> Should receive v2.0.0
                var siteTarget = (await targetRepo.GetByReleaseIdAsync(relSite.Id, track: true)).First();
                siteTarget.Disable();
                await dbContext.SaveChangesAsync();

                var eval4 = await eligibilityService.EvaluateWorkstationEligibilityAsync(ws.Id, "v1.0.0");
                Assert.True(eval4.IsEligible);
                Assert.Equal("v2.0.0", eval4.ReleaseContract!.Version);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [Fact]
        public async Task StagedRollout_MonotonicExpansion_PreservesPreviouslyEligibleWorkstations()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"sayra_rollout_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            try
            {
                using var dbContext = CreateDbContext();

                var releaseRepo = new UpdateReleaseRepository(dbContext);
                var targetRepo = new UpdateTargetRepository(dbContext);
                var keyRegistryRepo = new ConfigurationKeyRegistryRepository(dbContext);
                var wsRepo = new Repository<Workstation>(dbContext);
                var orgRepo = new Repository<Organization>(dbContext);
                var groupRepo = new WorkstationGroupRepository(dbContext);

                var storage = CreateTestStorage(tempDir);
                var cryptoService = new CryptographicService();

                var privateKeyProvider = new SigningPrivateKeyProvider(Options.Create(new SecurityOptions()));
                var (keyId, pubPem, privPem) = SigningPrivateKeyProvider.GetOrCreateEphemeralKeyPair("key-roll-01");
                privateKeyProvider.RegisterTestKeyPair(keyId, pubPem, privPem);
                await keyRegistryRepo.AddAsync(ConfigurationSigningKey.Create(keyId, pubPem, "RSA-SHA256", SigningKeyStatus.Active));
                await dbContext.SaveChangesAsync();

                var updateSigningKeyProvider = new UpdateSigningKeyProvider(keyRegistryRepo, privateKeyProvider);
                var signingService = new UpdateSigningService(updateSigningKeyProvider, cryptoService);
                var eligibilityService = new UpdateEligibilityService(wsRepo, groupRepo, releaseRepo, targetRepo);

                var org = new Organization { Name = "Rollout Org", Code = "ROLLORG", Status = "Active" };
                await orgRepo.AddAsync(org);

                var (release, _) = await CreatePublishedReleaseAsync(dbContext, org.Id, "v3.0.0", storage, signingService, keyId);

                // Create 20 workstations
                var workstations = new List<Workstation>();
                for (int i = 1; i <= 20; i++)
                {
                    var ws = new Workstation
                    {
                        PcId = $"PC-ROLLOUT-{i:D3}",
                        Hostname = $"pc-rollout-{i:D3}",
                        IpAddress = $"192.168.1.{i}",
                        MacAddress = $"00:11:22:33:44:{i:X2}",
                        OrganizationEntityId = org.Id,
                        Status = "Available",
                        ClientVersion = "v1.0.0"
                    };
                    await wsRepo.AddAsync(ws);
                    workstations.Add(ws);
                }
                await dbContext.SaveChangesAsync();

                // Initial Global Target at 10% Rollout
                var target = UpdateTarget.CreateGlobal(org.Id, release.Id, 10);
                await targetRepo.AddAsync(target);
                await dbContext.SaveChangesAsync();

                var eligibleAt10Percent = new List<Workstation>();
                foreach (var ws in workstations)
                {
                    var eval = await eligibilityService.EvaluateWorkstationEligibilityAsync(ws.Id, "v1.0.0");
                    if (eval.IsEligible)
                    {
                        eligibleAt10Percent.Add(ws);
                    }
                }

                // Increase Rollout to 50%
                target.SetRolloutPercentage(50);
                await dbContext.SaveChangesAsync();

                var eligibleAt50Percent = new List<Workstation>();
                foreach (var ws in workstations)
                {
                    var eval = await eligibilityService.EvaluateWorkstationEligibilityAsync(ws.Id, "v1.0.0");
                    if (eval.IsEligible)
                    {
                        eligibleAt50Percent.Add(ws);
                    }
                }

                // Monotonic Invariant: Every workstation eligible at 10% MUST remain eligible at 50%
                foreach (var ws10 in eligibleAt10Percent)
                {
                    Assert.Contains(eligibleAt50Percent, w => w.Id == ws10.Id);
                }

                Assert.True(eligibleAt50Percent.Count >= eligibleAt10Percent.Count);

                // Increase Rollout to 100%
                target.SetRolloutPercentage(100);
                await dbContext.SaveChangesAsync();

                foreach (var ws in workstations)
                {
                    var eval = await eligibilityService.EvaluateWorkstationEligibilityAsync(ws.Id, "v1.0.0");
                    Assert.True(eval.IsEligible, $"Workstation '{ws.PcId}' must be eligible at 100% rollout.");
                }
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [Fact]
        public async Task MultiTenantBoundary_CrossOrganizationAccess_IsDenied()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"sayra_tenant_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            try
            {
                using var dbContext = CreateDbContext();

                var releaseRepo = new UpdateReleaseRepository(dbContext);
                var targetRepo = new UpdateTargetRepository(dbContext);
                var keyRegistryRepo = new ConfigurationKeyRegistryRepository(dbContext);
                var wsRepo = new Repository<Workstation>(dbContext);
                var orgRepo = new Repository<Organization>(dbContext);
                var groupRepo = new WorkstationGroupRepository(dbContext);
                var secEventRepo = new Repository<SecurityEvent>(dbContext);

                var storage = CreateTestStorage(tempDir);
                var cryptoService = new CryptographicService();
                var secEventService = new SecurityEventService(secEventRepo, dbContext, NullLogger<SecurityEventService>.Instance);

                var privateKeyProvider = new SigningPrivateKeyProvider(Options.Create(new SecurityOptions()));
                var (keyId, pubPem, privPem) = SigningPrivateKeyProvider.GetOrCreateEphemeralKeyPair("key-tenant-01");
                privateKeyProvider.RegisterTestKeyPair(keyId, pubPem, privPem);
                await keyRegistryRepo.AddAsync(ConfigurationSigningKey.Create(keyId, pubPem, "RSA-SHA256", SigningKeyStatus.Active));
                await dbContext.SaveChangesAsync();

                var updateSigningKeyProvider = new UpdateSigningKeyProvider(keyRegistryRepo, privateKeyProvider);
                var signingService = new UpdateSigningService(updateSigningKeyProvider, cryptoService);
                var eligibilityService = new UpdateEligibilityService(wsRepo, groupRepo, releaseRepo, targetRepo);
                var manifestService = new UpdateManifestService(eligibilityService, wsRepo, NullLogger<UpdateManifestService>.Instance, secEventService);

                // Org A and Org B
                var orgA = new Organization { Name = "Org Alpha", Code = "ORGA", Status = "Active" };
                var orgB = new Organization { Name = "Org Beta", Code = "ORGB", Status = "Active" };
                await orgRepo.AddAsync(orgA);
                await orgRepo.AddAsync(orgB);

                // Workstation belongs to Org A
                var wsA = new Workstation
                {
                    PcId = "PC-ORGA-001",
                    Hostname = "pc-orga-001",
                    IpAddress = "192.168.1.15",
                    MacAddress = "00:11:22:33:44:99",
                    OrganizationEntityId = orgA.Id,
                    Status = "Available",
                    ClientVersion = "v1.0.0"
                };
                await wsRepo.AddAsync(wsA);
                await dbContext.SaveChangesAsync();

                // Release published under Org B
                var (releaseB, _) = await CreatePublishedReleaseAsync(dbContext, orgB.Id, "v5.0.0", storage, signingService, keyId);
                await targetRepo.AddAsync(UpdateTarget.CreateGlobal(orgB.Id, releaseB.Id, 100));
                await dbContext.SaveChangesAsync();

                // Workstation in Org A requesting manifest MUST NOT receive release from Org B -> HTTP 204 No Content / Not Available
                var userPrincipalA = new UserPrincipal
                {
                    UserId = Guid.NewGuid(),
                    Username = "user_orga",
                    IsAuthenticated = true,
                    PcId = wsA.PcId,
                    OrganizationId = orgA.Id
                };

                var manifestReq = new UpdateManifestRequest
                {
                    Principal = userPrincipalA,
                    ReportedVersion = "v1.0.0"
                };

                var manifestRes = await manifestService.GetManifestAsync(manifestReq);
                Assert.False(manifestRes.UpdateAvailable);

                // Attempting cross-organization caller principal mismatch (Principal Org B requesting manifest for Workstation Org A) -> Rejected
                var userPrincipalMismatched = new UserPrincipal
                {
                    UserId = Guid.NewGuid(),
                    Username = "user_orgb",
                    IsAuthenticated = true,
                    PcId = wsA.PcId,
                    OrganizationId = orgB.Id // Org B principal vs Org A workstation
                };

                var manifestResMismatched = await manifestService.GetManifestAsync(new UpdateManifestRequest
                {
                    Principal = userPrincipalMismatched,
                    ReportedVersion = "v1.0.0"
                });

                Assert.False(manifestResMismatched.UpdateAvailable);
                Assert.Equal(EligibilityReasonCodes.OrganizationMismatch, manifestResMismatched.ReasonCode);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [Fact]
        public async Task MinimumSupportedVersion_EnforcesMandatoryThreshold()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"sayra_minver_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            try
            {
                using var dbContext = CreateDbContext();

                var releaseRepo = new UpdateReleaseRepository(dbContext);
                var targetRepo = new UpdateTargetRepository(dbContext);
                var keyRegistryRepo = new ConfigurationKeyRegistryRepository(dbContext);
                var wsRepo = new Repository<Workstation>(dbContext);
                var orgRepo = new Repository<Organization>(dbContext);
                var groupRepo = new WorkstationGroupRepository(dbContext);

                var storage = CreateTestStorage(tempDir);
                var cryptoService = new CryptographicService();

                var privateKeyProvider = new SigningPrivateKeyProvider(Options.Create(new SecurityOptions()));
                var (keyId, pubPem, privPem) = SigningPrivateKeyProvider.GetOrCreateEphemeralKeyPair("key-minver-01");
                privateKeyProvider.RegisterTestKeyPair(keyId, pubPem, privPem);
                await keyRegistryRepo.AddAsync(ConfigurationSigningKey.Create(keyId, pubPem, "RSA-SHA256", SigningKeyStatus.Active));
                await dbContext.SaveChangesAsync();

                var updateSigningKeyProvider = new UpdateSigningKeyProvider(keyRegistryRepo, privateKeyProvider);
                var signingService = new UpdateSigningService(updateSigningKeyProvider, cryptoService);
                var eligibilityService = new UpdateEligibilityService(wsRepo, groupRepo, releaseRepo, targetRepo);

                var org = new Organization { Name = "MinVer Org", Code = "MINORG", Status = "Active" };
                await orgRepo.AddAsync(org);

                var wsOld = new Workstation
                {
                    PcId = "PC-OLD-001",
                    Hostname = "pc-old-001",
                    IpAddress = "192.168.1.50",
                    MacAddress = "00:11:22:33:44:00",
                    OrganizationEntityId = org.Id,
                    Status = "Available",
                    ClientVersion = "v0.9.0"
                };

                var wsNew = new Workstation
                {
                    PcId = "PC-NEW-001",
                    Hostname = "pc-new-001",
                    IpAddress = "192.168.1.51",
                    MacAddress = "00:11:22:33:44:01",
                    OrganizationEntityId = org.Id,
                    Status = "Available",
                    ClientVersion = "v1.5.0"
                };

                await wsRepo.AddAsync(wsOld);
                await wsRepo.AddAsync(wsNew);
                await dbContext.SaveChangesAsync();

                var (release, _) = await CreatePublishedReleaseAsync(dbContext, org.Id, "v2.0.0", storage, signingService, keyId);

                // Global Target with MinimumSupportedVersion = "v1.0.0" at 0% rollout!
                var target = UpdateTarget.CreateGlobal(org.Id, release.Id, rolloutPercentage: 0, minimumSupportedVersion: "v1.0.0");
                await targetRepo.AddAsync(target);
                await dbContext.SaveChangesAsync();

                // Workstation wsOld is on "v0.9.0" (below minimum "v1.0.0") -> Mandatory override bypasses 0% rollout!
                var evalOld = await eligibilityService.EvaluateWorkstationEligibilityAsync(wsOld.Id, "v0.9.0");
                Assert.True(evalOld.IsEligible);
                Assert.True(evalOld.IsMandatory);

                // Workstation wsNew is on "v1.5.0" (above minimum "v1.0.0") -> Evaluates 0% rollout -> Ineligible (RolloutNotEligible)
                var evalNew = await eligibilityService.EvaluateWorkstationEligibilityAsync(wsNew.Id, "v1.5.0");
                Assert.False(evalNew.IsEligible);
                Assert.Equal(EligibilityReasonCodes.RolloutNotEligible, evalNew.ReasonCode);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }
    }
}
