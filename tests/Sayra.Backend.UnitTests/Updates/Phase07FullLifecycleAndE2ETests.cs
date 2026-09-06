using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
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
    public class Phase07FullLifecycleAndE2ETests
    {
        private static ApplicationDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: $"E2E_Update_{Guid.NewGuid():N}")
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
                MaxArtifactSizeBytes = 100 * 1024 * 1024 // 100 MB
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

        [Fact]
        public async Task Full_Update_Lifecycle_Flow_Succeeds_EndToEnd()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"sayra_e2e_update_{Guid.NewGuid():N}");
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
                var packageValidator = new UpdatePackageValidator(Options.Create(new UpdateValidationOptions()), NullLogger<UpdatePackageValidator>.Instance);
                var hashService = new UpdateHashService();
                var cryptoService = new CryptographicService();
                var secEventService = new SecurityEventService(secEventRepo, dbContext, NullLogger<SecurityEventService>.Instance);

                var authMock = new Mock<IAuthorizationService>();
                authMock.Setup(a => a.AuthorizeAsync(It.IsAny<UserPrincipal>(), It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(AuthorizationResult.Allowed());

                var privateKeyProvider = new SigningPrivateKeyProvider(Options.Create(new SecurityOptions()));

                var (ephemeralKeyId, ephemeralPublicPem, ephemeralPrivatePem) = SigningPrivateKeyProvider.GetOrCreateEphemeralKeyPair("key-update-e2e-01");
                privateKeyProvider.RegisterTestKeyPair("key-update-e2e-01", ephemeralPublicPem, ephemeralPrivatePem);
                await keyRegistryRepo.AddAsync(ConfigurationSigningKey.Create("key-update-e2e-01", ephemeralPublicPem, "RSA-SHA256", SigningKeyStatus.Active));
                await dbContext.SaveChangesAsync();

                var updateSigningKeyProvider = new UpdateSigningKeyProvider(keyRegistryRepo, privateKeyProvider);
                var signingService = new UpdateSigningService(updateSigningKeyProvider, cryptoService);

                var eligibilityService = new UpdateEligibilityService(
                    wsRepo, groupRepo, releaseRepo, targetRepo);

                var manifestService = new UpdateManifestService(
                    eligibilityService, wsRepo, NullLogger<UpdateManifestService>.Instance, secEventService);

                var downloadService = new UpdateDownloadService(
                    packageRepo, releaseRepo, wsRepo, eligibilityService, storage,
                    NullLogger<UpdateDownloadService>.Instance, secEventService);

                // 1. Seed Organization, Site & Workstation (Version v1.0.0)
                var org = new Organization { Name = "E2E Update Org", Code = "E2EORG", Status = "Active" };
                await orgRepo.AddAsync(org);

                var site = new Site { OrganizationId = org.Id, Name = "Arena One", Code = "ARENA1", Status = "Active" };
                await siteRepo.AddAsync(site);

                var ws = new Workstation
                {
                    PcId = "PC-UPDATE-001",
                    Hostname = "pc-update-001",
                    IpAddress = "192.168.1.101",
                    MacAddress = "00:11:22:33:44:AA",
                    OrganizationEntityId = org.Id,
                    SiteEntityId = site.Id,
                    Status = "Available"
                };
                await wsRepo.AddAsync(ws);
                await dbContext.SaveChangesAsync();

                var adminPrincipal = new UserPrincipal
                {
                    UserId = Guid.NewGuid(),
                    Username = "admin_user",
                    OrganizationId = org.Id,
                    Permissions = new List<string> { PermissionCatalog.ManageUpdates, PermissionCatalog.ViewUpdates },
                    IsAuthenticated = true
                };

                // 2. Create Release v2.0.0 (Draft)
                var createReleaseHandler = new CreateUpdateReleaseCommandHandler(
                    releaseRepo, dbContext, authMock.Object, secEventService);

                var createReleaseRes = await createReleaseHandler.HandleAsync(new CreateUpdateReleaseCommand
                {
                    OrganizationId = org.Id,
                    Version = "v2.0.0",
                    ReleaseType = UpdateReleaseType.Standard,
                    ReleaseNotes = "Release notes v2.0.0",
                    Principal = adminPrincipal
                }, CancellationToken.None);
                Assert.True(createReleaseRes.IsSuccess);
                var releaseContract = createReleaseRes.Value!;

                // 3. Create Artifact & Upload
                byte[] packageBytes = CreateZipContent("SayraClient.exe", "BINARY_CONTENT_V2_0_0");
                using var artifactStream = new MemoryStream(packageBytes);

                var uploadHandler = new UploadUpdatePackageCommandHandler(
                    releaseRepo, packageRepo, dbContext, storage, packageValidator, hashService, authMock.Object, secEventService,
                    NullLogger<UploadUpdatePackageCommandHandler>.Instance);

                var uploadRes = await uploadHandler.HandleAsync(new UploadUpdatePackageCommand
                {
                    ReleaseId = releaseContract.ReleaseId,
                    FileName = "SayraClient-v2.0.0.spk",
                    ContentStream = artifactStream,
                    PackageType = UpdatePackageType.Spk,
                    Principal = adminPrincipal
                }, CancellationToken.None);
                Assert.True(uploadRes.IsSuccess);
                var packageMetadata = uploadRes.Value!;

                // 4. Artifact Validation
                var validateHandler = new ValidateUpdatePackageCommandHandler(
                    packageRepo, releaseRepo, dbContext, storage, packageValidator, hashService, authMock.Object);

                var valRes = await validateHandler.HandleAsync(new ValidateUpdatePackageCommand
                {
                    PackageId = packageMetadata.PackageId,
                    Principal = adminPrincipal
                }, CancellationToken.None);
                Assert.True(valRes.IsSuccess);

                // 5. Digital Signing (RSA-SHA256)
                var signHandler = new SignUpdatePackageCommandHandler(
                    packageRepo, releaseRepo, dbContext, storage, hashService, signingService, authMock.Object, secEventService);

                var signRes = await signHandler.HandleAsync(new SignUpdatePackageCommand
                {
                    PackageId = packageMetadata.PackageId,
                    KeyId = "key-update-e2e-01",
                    Principal = adminPrincipal
                }, CancellationToken.None);
                Assert.True(signRes.IsSuccess);
                var signedPackageMetadata = signRes.Value!;
                Assert.False(string.IsNullOrWhiteSpace(signedPackageMetadata.ChecksumSha256));
                Assert.False(string.IsNullOrWhiteSpace(signedPackageMetadata.Signature));

                // 6. Mark Release Ready & Publish
                var prepareReleaseHandler = new PrepareUpdateReleaseCommandHandler(
                    releaseRepo, dbContext, storage, hashService, authMock.Object, secEventService);
                var prepRes = await prepareReleaseHandler.HandleAsync(new PrepareUpdateReleaseCommand
                {
                    ReleaseId = releaseContract.ReleaseId,
                    Principal = adminPrincipal
                }, CancellationToken.None);
                Assert.True(prepRes.IsSuccess);

                // Assign Target (Global Target 100% Rollout)
                var createTargetHandler = new CreateUpdateTargetCommandHandler(
                    targetRepo, releaseRepo, orgRepo, siteRepo, groupRepo, wsRepo, dbContext, authMock.Object, secEventService);
                var targetRes = await createTargetHandler.HandleAsync(new CreateUpdateTargetCommand
                {
                    ReleaseId = releaseContract.ReleaseId,
                    TargetType = ConfigurationTargetType.Global,
                    RolloutPercentage = 100,
                    Principal = adminPrincipal
                }, CancellationToken.None);
                Assert.True(targetRes.IsSuccess);

                // Publish Release
                var publishReleaseHandler = new PublishUpdateReleaseCommandHandler(
                    releaseRepo, dbContext, storage, hashService, authMock.Object, secEventService);
                var pubRes = await publishReleaseHandler.HandleAsync(new PublishUpdateReleaseCommand
                {
                    ReleaseId = releaseContract.ReleaseId,
                    Principal = adminPrincipal
                }, CancellationToken.None);
                Assert.True(pubRes.IsSuccess);
                Assert.Equal("Published", pubRes.Value!.Status);

                // 7. Workstation Discovery & Manifest Request
                var userPrincipal = new UserPrincipal
                {
                    UserId = Guid.NewGuid(),
                    Username = "workstation_user",
                    IsAuthenticated = true,
                    PcId = ws.PcId,
                    OrganizationId = org.Id,
                    SiteId = site.Id
                };

                var manifestReq = new UpdateManifestRequest
                {
                    Principal = userPrincipal,
                    ReportedVersion = "v1.0.0",
                    OsVersion = "Windows 11",
                    Architecture = "x64",
                    DownloadBaseUrl = "https://updates.sayra.io"
                };

                var manifestRes = await manifestService.GetManifestAsync(manifestReq);
                Assert.True(manifestRes.UpdateAvailable);
                Assert.NotNull(manifestRes.Manifest);
                Assert.Equal("v2.0.0", manifestRes.Manifest.Version);
                Assert.Equal(signedPackageMetadata.ChecksumSha256, manifestRes.Manifest.Checksum);
                Assert.Equal(signedPackageMetadata.Signature, manifestRes.Manifest.Signature);
                Assert.Contains(signedPackageMetadata.PackageId.ToString(), manifestRes.Manifest.PackageUrl);

                // 8. Secure Streaming Download (Full Download)
                var downloadReq = new UpdateDownloadRequest
                {
                    Principal = userPrincipal,
                    PackageId = signedPackageMetadata.PackageId,
                    ReportedVersion = "v1.0.0",
                    OsVersion = "Windows 11",
                    Architecture = "x64",
                    RangeHeader = null
                };

                using (var prepDownload = await downloadService.PrepareDownloadAsync(downloadReq))
                {
                    Assert.True(prepDownload.IsSuccess);
                    Assert.False(prepDownload.Range.IsRangeRequest);
                    Assert.NotNull(prepDownload.ContentStream);

                    using var msDownloaded = new MemoryStream();
                    await prepDownload.ContentStream.CopyToAsync(msDownloaded);
                    byte[] downloadedBytes = msDownloaded.ToArray();

                    Assert.Equal(packageBytes.Length, downloadedBytes.Length);
                    Assert.Equal(packageBytes, downloadedBytes);

                    // Client-Side Hash Verification Boundary
                    using var msHash = new MemoryStream(downloadedBytes);
                    string downloadedHash = await hashService.ComputeSha256Async(msHash);
                    Assert.Equal(signedPackageMetadata.ChecksumSha256, downloadedHash);

                    // Client-Side Signature Verification Boundary
                    var packageEntity = await packageRepo.GetByIdAsync(signedPackageMetadata.PackageId);
                    Assert.NotNull(packageEntity);

                    var verifyRes = await signingService.VerifyPackageAsync(packageEntity!);
                    Assert.True(verifyRes.IsValid, verifyRes.ErrorMessage);
                }

                // 9. Resumed Download (HTTP Range Header)
                var rangeDownloadReq = new UpdateDownloadRequest
                {
                    Principal = userPrincipal,
                    PackageId = signedPackageMetadata.PackageId,
                    ReportedVersion = "v1.0.0",
                    OsVersion = "Windows 11",
                    Architecture = "x64",
                    RangeHeader = $"bytes=10-{packageBytes.Length - 1}"
                };

                using (var prepRangeDownload = await downloadService.PrepareDownloadAsync(rangeDownloadReq))
                {
                    Assert.True(prepRangeDownload.IsSuccess);
                    Assert.True(prepRangeDownload.Range.IsRangeRequest);
                    Assert.Equal(10, prepRangeDownload.Range.Start);
                    Assert.Equal(packageBytes.Length - 1, prepRangeDownload.Range.End);

                    using var msRange = new MemoryStream();
                    byte[] buffer = new byte[1024];
                    int read;
                    long remaining = prepRangeDownload.Range.ServedLength;
                    while (remaining > 0 && (read = await prepRangeDownload.ContentStream!.ReadAsync(buffer, 0, (int)Math.Min(buffer.Length, remaining))) > 0)
                    {
                        msRange.Write(buffer, 0, read);
                        remaining -= read;
                    }

                    byte[] resumedChunk = msRange.ToArray();
                    byte[] expectedChunk = packageBytes.Skip(10).ToArray();
                    Assert.Equal(expectedChunk, resumedChunk);
                }

                // 10. Emergency Revocation
                var revokeHandler = new RevokeUpdateReleaseCommandHandler(
                    releaseRepo, dbContext, authMock.Object, secEventService);
                var revokeRes = await revokeHandler.HandleAsync(new RevokeUpdateReleaseCommand
                {
                    ReleaseId = releaseContract.ReleaseId,
                    Reason = "Security flaw detected",
                    Principal = adminPrincipal
                }, CancellationToken.None);
                Assert.True(revokeRes.IsSuccess);

                // Subsequent Manifest Request MUST NOT return revoked release -> HTTP 204 No Content / Not Available
                var manifestAfterRevokeRes = await manifestService.GetManifestAsync(manifestReq);
                Assert.False(manifestAfterRevokeRes.UpdateAvailable);

                // Subsequent Download Request MUST BE REJECTED -> 403 Forbidden
                using (var downloadAfterRevoke = await downloadService.PrepareDownloadAsync(downloadReq))
                {
                    Assert.False(downloadAfterRevoke.IsSuccess);
                    Assert.Equal(403, downloadAfterRevoke.HttpStatusCode);
                }

                // Verify Security Audit Events recorded
                var events = await secEventRepo.FindAsync(e => e.DeviceId == ws.PcId, track: false);
                Assert.NotEmpty(events);
                Assert.Contains(events, e => e.EventType == "UPDATE_MANIFEST_DISCOVERED");
                Assert.Contains(events, e => e.EventType == "UPDATE_DOWNLOAD_STARTED");
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
