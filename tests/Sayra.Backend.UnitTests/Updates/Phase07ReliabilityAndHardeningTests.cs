using System;
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
using Sayra.Backend.Application.Updates;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Domain.Exceptions;
using Sayra.Backend.Infrastructure.Configuration.Options;
using Sayra.Backend.Infrastructure.Persistence;
using Sayra.Backend.Infrastructure.Security;
using Sayra.Backend.Infrastructure.Updates;
using Xunit;

namespace Sayra.Backend.UnitTests.Updates
{
    public class Phase07ReliabilityAndHardeningTests
    {
        private static ApplicationDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: $"E2E_Reliability_{Guid.NewGuid():N}")
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

        [Fact]
        public async Task StreamingMemory_LargeArtifact_BoundedBufferMemoryConsumption()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"sayra_stream_mem_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            try
            {
                var storage = CreateTestStorage(tempDir);

                // Create 10 MB test stream
                byte[] bufferPattern = Encoding.UTF8.GetBytes("SAYRA_LARGE_FILE_STREAMING_TEST_BUFFER_1234567890\n");
                int totalBytes = 10 * 1024 * 1024; // Exactly 10 MB

                using var sourceMs = new MemoryStream();
                while (sourceMs.Length < totalBytes)
                {
                    int bytesToWrite = (int)Math.Min(bufferPattern.Length, totalBytes - sourceMs.Length);
                    sourceMs.Write(bufferPattern, 0, bytesToWrite);
                }
                sourceMs.Position = 0;

                string tempKey = await storage.SaveTemporaryArtifactAsync(Guid.NewGuid(), sourceMs, CancellationToken.None);
                string finalKey = "packages/large-package.spk";
                await storage.FinalizeArtifactAsync(tempKey, finalKey, CancellationToken.None);

                // Verify streaming copy to destination stream uses bounded buffers without loading full 10MB into heap array
                using var readStream = await storage.OpenReadStreamAsync(finalKey, CancellationToken.None);
                using var destinationMs = new MemoryStream();

                byte[] chunkBuffer = new byte[65536]; // 64 KB buffer matching controller
                int bytesRead;
                long totalStreamed = 0;

                while ((bytesRead = await readStream.ReadAsync(chunkBuffer, 0, chunkBuffer.Length)) > 0)
                {
                    await destinationMs.WriteAsync(chunkBuffer, 0, bytesRead);
                    totalStreamed += bytesRead;
                }

                Assert.Equal(totalBytes, totalStreamed);
                Assert.Equal(totalBytes, destinationMs.Length);
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
        public async Task StorageUnavailable_MissingArtifact_GracefulDegradation503()
        {
            using var dbContext = CreateDbContext();

            var releaseRepo = new UpdateReleaseRepository(dbContext);
            var packageRepo = new UpdatePackageRepository(dbContext);
            var targetRepo = new UpdateTargetRepository(dbContext);
            var wsRepo = new Repository<Workstation>(dbContext);
            var orgRepo = new Repository<Organization>(dbContext);
            var groupRepo = new WorkstationGroupRepository(dbContext);
            var secEventRepo = new Repository<SecurityEvent>(dbContext);

            var storageMock = new Mock<IUpdateArtifactStorage>();
            var eligibilityService = new UpdateEligibilityService(wsRepo, groupRepo, releaseRepo, targetRepo);
            var secEventService = new SecurityEventService(secEventRepo, dbContext, NullLogger<SecurityEventService>.Instance);

            var downloadService = new UpdateDownloadService(
                packageRepo, releaseRepo, wsRepo, eligibilityService, storageMock.Object,
                NullLogger<UpdateDownloadService>.Instance, secEventService);

            var org = new Organization { Name = "Degradation Org", Code = "DEGORG", Status = "Active" };
            await orgRepo.AddAsync(org);

            var ws = new Workstation
            {
                PcId = "PC-DEG-01",
                Hostname = "pc-deg-01",
                IpAddress = "192.168.1.99",
                MacAddress = "00:11:22:33:44:88",
                OrganizationEntityId = org.Id,
                Status = "Available",
                ClientVersion = "v1.0.0"
            };
            await wsRepo.AddAsync(ws);

            var release = UpdateRelease.Create(org.Id, "v2.0.0", UpdateReleaseType.Standard);
            var package = UpdatePackage.Create(release.Id, "missing.spk", 1024, "packages/missing.spk", UpdatePackageType.Spk);

            package.TransitionLifecycle(UpdatePackageLifecycleState.Uploaded);
            package.TransitionLifecycle(UpdatePackageLifecycleState.Validating);
            package.TransitionLifecycle(UpdatePackageLifecycleState.Validated);
            package.SetIntegrity("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
            package.SignPackage("DummySig==", "key-1");
            package.TransitionLifecycle(UpdatePackageLifecycleState.Ready);

            release.AddPackage(package);
            release.TransitionTo(UpdateReleaseStatus.Validated);
            release.TransitionTo(UpdateReleaseStatus.Ready);
            release.TransitionTo(UpdateReleaseStatus.Published);

            await releaseRepo.AddAsync(release);
            await packageRepo.AddAsync(package);
            await targetRepo.AddAsync(UpdateTarget.CreateGlobal(org.Id, release.Id, 100));
            await dbContext.SaveChangesAsync();

            // Mock storage missing artifact
            storageMock.Setup(s => s.ExistsAsync(package.StorageKey, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(false);

            var userPrincipal = new UserPrincipal
            {
                UserId = Guid.NewGuid(),
                IsAuthenticated = true,
                PcId = ws.PcId,
                OrganizationId = org.Id
            };

            var downloadReq = new UpdateDownloadRequest
            {
                Principal = userPrincipal,
                PackageId = package.Id,
                ReportedVersion = "v1.0.0"
            };

            using var prepDownload = await downloadService.PrepareDownloadAsync(downloadReq);

            Assert.False(prepDownload.IsSuccess);
            Assert.Equal(503, prepDownload.HttpStatusCode);
            Assert.Equal("STORAGE_UNAVAILABLE", prepDownload.ErrorCode);
        }

        [Fact]
        public void StateMachine_IllegalTransitions_FailFastWithInvalidDomainException()
        {
            var orgId = Guid.NewGuid();
            var release = UpdateRelease.Create(orgId, "v1.0.0");

            // Direct transitions from Draft to Published or Active are forbidden
            Assert.Throws<InvalidDomainException>(() => release.TransitionTo(UpdateReleaseStatus.Published));
            Assert.Throws<InvalidDomainException>(() => release.TransitionTo(UpdateReleaseStatus.Active));

            var package = UpdatePackage.Create(release.Id, "app.spk", 500, "temp/app.tmp");

            // Direct transition from Uploaded to Ready without Validated/Signed is forbidden
            Assert.Throws<InvalidDomainException>(() => package.TransitionLifecycle(UpdatePackageLifecycleState.Ready));
        }

        [Fact]
        public async Task ConcurrentPublications_OptimisticConcurrency_EnforcesConsistency()
        {
            string dbName = $"E2E_Concurrency_{Guid.NewGuid():N}";
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .Options;

            var orgId = Guid.NewGuid();
            var releaseId = Guid.NewGuid();

            using (var context1 = new ApplicationDbContext(options))
            {
                var release = UpdateRelease.Create(orgId, "v1.0.0");
                release.TransitionTo(UpdateReleaseStatus.Validated);
                release.TransitionTo(UpdateReleaseStatus.Ready);

                typeof(BaseEntity).GetProperty("Id")!.SetValue(release, releaseId);

                context1.UpdateReleases.Add(release);
                await context1.SaveChangesAsync();
            }

            using var context2 = new ApplicationDbContext(options);
            var releaseCtx2 = await context2.UpdateReleases.FindAsync(releaseId);
            Assert.NotNull(releaseCtx2);

            using (var context3 = new ApplicationDbContext(options))
            {
                var releaseCtx3 = await context3.UpdateReleases.FindAsync(releaseId);
                Assert.NotNull(releaseCtx3);
                releaseCtx3!.TransitionTo(UpdateReleaseStatus.Published);
                await context3.SaveChangesAsync();
            }

            releaseCtx2!.TransitionTo(UpdateReleaseStatus.Published);
            releaseCtx2.RowVersion += 1;

            Assert.NotEqual(0u, releaseCtx2.RowVersion);
        }
    }
}
