using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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

#nullable enable

namespace Sayra.Backend.UnitTests
{
    public class UpdateDownloadUnitTests
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
            long packageSize = 1048576,
            string? customSha256 = null,
            UpdateReleaseType releaseType = UpdateReleaseType.Standard,
            UpdateReleaseStatus status = UpdateReleaseStatus.Active)
        {
            var release = UpdateRelease.Create(orgId, version, releaseType, $"Release notes for {version}", "admin");
            var package = UpdatePackage.Create(release.Id, $"sayra-client-{version}.spk", packageSize, $"packages/{release.Id}/app.spk");

            package.TransitionLifecycle(UpdatePackageLifecycleState.Uploaded);
            package.TransitionLifecycle(UpdatePackageLifecycleState.Validating);
            package.TransitionLifecycle(UpdatePackageLifecycleState.Validated);
            package.SetIntegrity(customSha256 ?? "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
            package.SignPackage("ValidRSASignatureBase64String==", "key-1");
            package.TransitionLifecycle(UpdatePackageLifecycleState.Ready);

            release.AddPackage(package);

            if (status == UpdateReleaseStatus.Draft)
            {
                // Leave as draft
            }
            else if (status == UpdateReleaseStatus.Validated)
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

        #region 1. HTTP Range Helper & Parsing Unit Tests

        [Fact]
        public void ParseRangeHeader_NullOrEmptyHeader_ReturnsFullRange()
        {
            long totalSize = 1000;
            var range = UpdateDownloadRangeHelper.ParseRangeHeader(null, totalSize);

            Assert.False(range.IsRangeRequest);
            Assert.True(range.IsSatisfiable);
            Assert.Equal(0, range.Start);
            Assert.Equal(999, range.End);
            Assert.Equal(1000, range.TotalSize);
            Assert.Equal(1000, range.ServedLength);
            Assert.Equal("bytes 0-999/1000", range.ContentRangeHeader);
        }

        [Fact]
        public void ParseRangeHeader_SingleByteStart_0_0_ReturnsSingleByteRange()
        {
            long totalSize = 1000;
            var range = UpdateDownloadRangeHelper.ParseRangeHeader("bytes=0-0", totalSize);

            Assert.True(range.IsRangeRequest);
            Assert.True(range.IsSatisfiable);
            Assert.Equal(0, range.Start);
            Assert.Equal(0, range.End);
            Assert.Equal(1, range.ServedLength);
            Assert.Equal("bytes 0-0/1000", range.ContentRangeHeader);
        }

        [Fact]
        public void ParseRangeHeader_ExplicitRange_0_99_ReturnsCorrectSegment()
        {
            long totalSize = 1000;
            var range = UpdateDownloadRangeHelper.ParseRangeHeader("bytes=0-99", totalSize);

            Assert.True(range.IsRangeRequest);
            Assert.True(range.IsSatisfiable);
            Assert.Equal(0, range.Start);
            Assert.Equal(99, range.End);
            Assert.Equal(100, range.ServedLength);
            Assert.Equal("bytes 0-99/1000", range.ContentRangeHeader);
        }

        [Fact]
        public void ParseRangeHeader_OpenEndedRange_100_ReturnsFromStartToEof()
        {
            long totalSize = 1000;
            var range = UpdateDownloadRangeHelper.ParseRangeHeader("bytes=100-", totalSize);

            Assert.True(range.IsRangeRequest);
            Assert.True(range.IsSatisfiable);
            Assert.Equal(100, range.Start);
            Assert.Equal(999, range.End);
            Assert.Equal(900, range.ServedLength);
            Assert.Equal("bytes 100-999/1000", range.ContentRangeHeader);
        }

        [Fact]
        public void ParseRangeHeader_SuffixRange_Minus200_ReturnsLast200Bytes()
        {
            long totalSize = 1000;
            var range = UpdateDownloadRangeHelper.ParseRangeHeader("bytes=-200", totalSize);

            Assert.True(range.IsRangeRequest);
            Assert.True(range.IsSatisfiable);
            Assert.Equal(800, range.Start);
            Assert.Equal(999, range.End);
            Assert.Equal(200, range.ServedLength);
            Assert.Equal("bytes 800-999/1000", range.ContentRangeHeader);
        }

        [Fact]
        public void ParseRangeHeader_SuffixRangeLargerThanFileSize_ClampsToFullRange()
        {
            long totalSize = 1000;
            var range = UpdateDownloadRangeHelper.ParseRangeHeader("bytes=-2000", totalSize);

            Assert.True(range.IsRangeRequest);
            Assert.True(range.IsSatisfiable);
            Assert.Equal(0, range.Start);
            Assert.Equal(999, range.End);
            Assert.Equal(1000, range.ServedLength);
        }

        [Fact]
        public void ParseRangeHeader_StartEqualToTotalSize_ReturnsUnsatisfiableRange()
        {
            long totalSize = 1000;
            var range = UpdateDownloadRangeHelper.ParseRangeHeader("bytes=1000-", totalSize);

            Assert.True(range.IsRangeRequest);
            Assert.False(range.IsSatisfiable);
            Assert.Equal("bytes */1000", range.ContentRangeHeader);
        }

        [Fact]
        public void ParseRangeHeader_StartGreaterThanTotalSize_ReturnsUnsatisfiableRange()
        {
            long totalSize = 1000;
            var range = UpdateDownloadRangeHelper.ParseRangeHeader("bytes=1500-", totalSize);

            Assert.True(range.IsRangeRequest);
            Assert.False(range.IsSatisfiable);
            Assert.Equal("bytes */1000", range.ContentRangeHeader);
        }

        [Fact]
        public void ParseRangeHeader_EndGreaterThanTotalSize_ClampsEndToEof()
        {
            long totalSize = 1000;
            var range = UpdateDownloadRangeHelper.ParseRangeHeader("bytes=0-2000", totalSize);

            Assert.True(range.IsRangeRequest);
            Assert.True(range.IsSatisfiable);
            Assert.Equal(0, range.Start);
            Assert.Equal(999, range.End);
            Assert.Equal(1000, range.ServedLength);
            Assert.Equal("bytes 0-999/1000", range.ContentRangeHeader);
        }

        [Fact]
        public void ParseRangeHeader_StartGreaterThanEnd_ReturnsUnsatisfiableRange()
        {
            long totalSize = 1000;
            var range = UpdateDownloadRangeHelper.ParseRangeHeader("bytes=500-100", totalSize);

            Assert.True(range.IsRangeRequest);
            Assert.False(range.IsSatisfiable);
            Assert.Equal("bytes */1000", range.ContentRangeHeader);
        }

        [Fact]
        public void ParseRangeHeader_MalformedOrOverflowHeader_ReturnsUnsatisfiableOrFallback()
        {
            long totalSize = 1000;

            var malformed = UpdateDownloadRangeHelper.ParseRangeHeader("bytes=abc-xyz", totalSize);
            Assert.False(malformed.IsSatisfiable);

            var overflow = UpdateDownloadRangeHelper.ParseRangeHeader("bytes=9999999999999999999999-", totalSize);
            Assert.False(overflow.IsSatisfiable);
        }

        #endregion

        #region 2. Authorization & Tenant Isolation Tests

        [Fact]
        public async Task PrepareDownloadAsync_UnauthenticatedPrincipal_Returns401()
        {
            using var dbContext = CreateInMemoryDbContext(Guid.NewGuid().ToString());
            var pkgRepo = new UpdatePackageRepository(dbContext);
            var relRepo = new UpdateReleaseRepository(dbContext);
            var wsRepo = new Repository<Workstation>(dbContext);
            var targetRepo = new UpdateTargetRepository(dbContext);
            var groupRepo = new WorkstationGroupRepository(dbContext);
            var storageMock = new Mock<IUpdateArtifactStorage>();

            var eligibilityService = new UpdateEligibilityService(wsRepo, groupRepo, relRepo, targetRepo);
            var service = new UpdateDownloadService(pkgRepo, relRepo, wsRepo, eligibilityService, storageMock.Object, NullLogger<UpdateDownloadService>.Instance);

            var request = new UpdateDownloadRequest
            {
                Principal = UserPrincipal.Anonymous,
                PackageId = Guid.NewGuid()
            };

            using var result = await service.PrepareDownloadAsync(request);

            Assert.False(result.IsSuccess);
            Assert.Equal(401, result.HttpStatusCode);
            Assert.Equal("UNAUTHORIZED", result.ErrorCode);
        }

        [Fact]
        public async Task PrepareDownloadAsync_CrossOrganizationAccess_Returns404ToPreventEnumeration()
        {
            using var dbContext = CreateInMemoryDbContext(Guid.NewGuid().ToString());
            var pkgRepo = new UpdatePackageRepository(dbContext);
            var relRepo = new UpdateReleaseRepository(dbContext);
            var wsRepo = new Repository<Workstation>(dbContext);
            var targetRepo = new UpdateTargetRepository(dbContext);
            var groupRepo = new WorkstationGroupRepository(dbContext);
            var storageMock = new Mock<IUpdateArtifactStorage>();

            var orgA = Guid.NewGuid();
            var orgB = Guid.NewGuid();

            var workstationOrgA = new Workstation
            {
                PcId = "PC-ORGA-DL",
                SiteId = "SITE-A",
                OrganizationEntityId = orgA,
                Hostname = "host-a",
                IpAddress = "10.0.0.20",
                MacAddress = "AA:BB:CC:DD:EE:20",
                ClientVersion = "1.0.0"
            };

            var (releaseOrgB, packageOrgB) = CreateSignedReleaseAndPackage(orgB, "2.0.0");

            await wsRepo.AddAsync(workstationOrgA);
            await relRepo.AddAsync(releaseOrgB);
            await pkgRepo.AddAsync(packageOrgB);
            await dbContext.SaveChangesAsync();

            var eligibilityService = new UpdateEligibilityService(wsRepo, groupRepo, relRepo, targetRepo);
            var service = new UpdateDownloadService(pkgRepo, relRepo, wsRepo, eligibilityService, storageMock.Object, NullLogger<UpdateDownloadService>.Instance);

            var principalOrgA = CreateWorkstationPrincipal(orgA, workstationOrgA.PcId, workstationOrgA.Id);
            var request = new UpdateDownloadRequest
            {
                Principal = principalOrgA,
                PackageId = packageOrgB.Id
            };

            using var result = await service.PrepareDownloadAsync(request);

            Assert.False(result.IsSuccess);
            Assert.Equal(404, result.HttpStatusCode);
            Assert.Equal("PACKAGE_NOT_FOUND", result.ErrorCode);
        }

        #endregion

        #region 3. Lifecycle State & Package Readiness Validation

        [Fact]
        public async Task PrepareDownloadAsync_RevokedRelease_Returns403()
        {
            using var dbContext = CreateInMemoryDbContext(Guid.NewGuid().ToString());
            var pkgRepo = new UpdatePackageRepository(dbContext);
            var relRepo = new UpdateReleaseRepository(dbContext);
            var wsRepo = new Repository<Workstation>(dbContext);
            var targetRepo = new UpdateTargetRepository(dbContext);
            var groupRepo = new WorkstationGroupRepository(dbContext);
            var storageMock = new Mock<IUpdateArtifactStorage>();

            var orgId = Guid.NewGuid();
            var workstation = new Workstation
            {
                PcId = "PC-REVOKED-DL",
                SiteId = "SITE-1",
                OrganizationEntityId = orgId,
                Hostname = "host-1",
                IpAddress = "10.0.0.21",
                MacAddress = "AA:BB:CC:DD:EE:21",
                ClientVersion = "1.0.0"
            };

            var (revokedRelease, revokedPackage) = CreateSignedReleaseAndPackage(orgId, "1.2.0", status: UpdateReleaseStatus.Revoked);
            var target = UpdateTarget.CreateGlobal(orgId, revokedRelease.Id, 100);

            await wsRepo.AddAsync(workstation);
            await relRepo.AddAsync(revokedRelease);
            await pkgRepo.AddAsync(revokedPackage);
            await targetRepo.AddAsync(target);
            await dbContext.SaveChangesAsync();

            var eligibilityService = new UpdateEligibilityService(wsRepo, groupRepo, relRepo, targetRepo);
            var service = new UpdateDownloadService(pkgRepo, relRepo, wsRepo, eligibilityService, storageMock.Object, NullLogger<UpdateDownloadService>.Instance);

            var principal = CreateWorkstationPrincipal(orgId, workstation.PcId, workstation.Id);
            var request = new UpdateDownloadRequest
            {
                Principal = principal,
                PackageId = revokedPackage.Id
            };

            using var result = await service.PrepareDownloadAsync(request);

            Assert.False(result.IsSuccess);
            Assert.Equal(403, result.HttpStatusCode);
            Assert.Equal(EligibilityReasonCodes.ReleaseNotActive, result.ErrorCode);
        }

        #endregion

        #region 4. Full Download, Partial Range & Resume Reconstruction with SHA-256

        [Fact]
        public async Task PrepareDownloadAsync_FullDownloadAndPartialResume_ReconstructsExactBinaryAndMatchesSha256()
        {
            using var dbContext = CreateInMemoryDbContext(Guid.NewGuid().ToString());
            var pkgRepo = new UpdatePackageRepository(dbContext);
            var relRepo = new UpdateReleaseRepository(dbContext);
            var wsRepo = new Repository<Workstation>(dbContext);
            var targetRepo = new UpdateTargetRepository(dbContext);
            var groupRepo = new WorkstationGroupRepository(dbContext);
            var storageMock = new Mock<IUpdateArtifactStorage>();

            var orgId = Guid.NewGuid();
            var workstation = new Workstation
            {
                PcId = "PC-STREAM-TEST",
                SiteId = "SITE-1",
                OrganizationEntityId = orgId,
                Hostname = "host-01",
                IpAddress = "10.0.0.22",
                MacAddress = "AA:BB:CC:DD:EE:22",
                ClientVersion = "1.0.0"
            };

            // Generate 1MB test artifact byte payload
            byte[] originalBinaryData = new byte[1024 * 1024]; // 1 MB
            new Random(42).NextBytes(originalBinaryData);

            string expectedSha256Hex;
            using (var sha256 = SHA256.Create())
            {
                byte[] hashBytes = sha256.ComputeHash(originalBinaryData);
                expectedSha256Hex = Convert.ToHexString(hashBytes).ToLowerInvariant();
            }

            var (release, package) = CreateSignedReleaseAndPackage(orgId, "1.5.0", packageSize: originalBinaryData.Length, customSha256: expectedSha256Hex);

            var target = UpdateTarget.CreateGlobal(orgId, release.Id, 100);

            await wsRepo.AddAsync(workstation);
            await relRepo.AddAsync(release);
            await pkgRepo.AddAsync(package);
            await targetRepo.AddAsync(target);
            await dbContext.SaveChangesAsync();

            storageMock.Setup(s => s.ExistsAsync(package.StorageKey, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(true);
            storageMock.Setup(s => s.GetArtifactSizeAsync(package.StorageKey, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(originalBinaryData.Length);

            // Mock stream creation that supports Seeking (like physical FileStream)
            storageMock.Setup(s => s.OpenReadStreamAsync(package.StorageKey, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(() => new MemoryStream(originalBinaryData, writable: false));

            var eligibilityService = new UpdateEligibilityService(wsRepo, groupRepo, relRepo, targetRepo);
            var service = new UpdateDownloadService(pkgRepo, relRepo, wsRepo, eligibilityService, storageMock.Object, NullLogger<UpdateDownloadService>.Instance);

            var principal = CreateWorkstationPrincipal(orgId, workstation.PcId, workstation.Id);

            // STEP A: Full Download Request (No Range)
            var fullRequest = new UpdateDownloadRequest
            {
                Principal = principal,
                PackageId = package.Id
            };

            using (var fullPrep = await service.PrepareDownloadAsync(fullRequest))
            {
                Assert.True(fullPrep.IsSuccess);
                Assert.Equal(200, fullPrep.HttpStatusCode);
                Assert.Equal(originalBinaryData.Length, fullPrep.TotalSize);
                Assert.NotNull(fullPrep.ContentStream);

                using var fullStreamCopy = new MemoryStream();
                await fullPrep.ContentStream.CopyToAsync(fullStreamCopy);
                byte[] downloadedBytes = fullStreamCopy.ToArray();

                Assert.Equal(originalBinaryData, downloadedBytes);

                using var sha256 = SHA256.Create();
                string actualDownloadedHash = Convert.ToHexString(sha256.ComputeHash(downloadedBytes)).ToLowerInvariant();
                Assert.Equal(expectedSha256Hex, actualDownloadedHash);
            }

            // STEP B: Resumable Download Test (Part 1: 0 to 524287, Part 2: 524288 to EOF)
            int splitPoint = 524288; // 512 KB

            var part1Request = new UpdateDownloadRequest
            {
                Principal = principal,
                PackageId = package.Id,
                RangeHeader = $"bytes=0-{splitPoint - 1}"
            };

            byte[] part1Bytes;
            using (var part1Prep = await service.PrepareDownloadAsync(part1Request))
            {
                Assert.True(part1Prep.IsSuccess);
                Assert.Equal(206, part1Prep.HttpStatusCode);
                Assert.Equal(splitPoint, part1Prep.Range.ServedLength);
                Assert.Equal($"bytes 0-{splitPoint - 1}/{originalBinaryData.Length}", part1Prep.Range.ContentRangeHeader);

                using var part1Copy = new MemoryStream();
                byte[] buffer = new byte[65536];
                long remaining = part1Prep.Range.ServedLength;
                while (remaining > 0)
                {
                    int toRead = (int)Math.Min(buffer.Length, remaining);
                    int read = await part1Prep.ContentStream!.ReadAsync(buffer, 0, toRead);
                    if (read <= 0) break;
                    await part1Copy.WriteAsync(buffer, 0, read);
                    remaining -= read;
                }
                part1Bytes = part1Copy.ToArray();
            }

            var part2Request = new UpdateDownloadRequest
            {
                Principal = principal,
                PackageId = package.Id,
                RangeHeader = $"bytes={splitPoint}-"
            };

            byte[] part2Bytes;
            using (var part2Prep = await service.PrepareDownloadAsync(part2Request))
            {
                Assert.True(part2Prep.IsSuccess);
                Assert.Equal(206, part2Prep.HttpStatusCode);
                Assert.Equal(originalBinaryData.Length - splitPoint, part2Prep.Range.ServedLength);
                Assert.Equal($"bytes {splitPoint}-{originalBinaryData.Length - 1}/{originalBinaryData.Length}", part2Prep.Range.ContentRangeHeader);

                using var part2Copy = new MemoryStream();
                byte[] buffer = new byte[65536];
                long remaining = part2Prep.Range.ServedLength;
                while (remaining > 0)
                {
                    int toRead = (int)Math.Min(buffer.Length, remaining);
                    int read = await part2Prep.ContentStream!.ReadAsync(buffer, 0, toRead);
                    if (read <= 0) break;
                    await part2Copy.WriteAsync(buffer, 0, read);
                    remaining -= read;
                }
                part2Bytes = part2Copy.ToArray();
            }

            // Combine part1 + part2 to reconstruct file
            byte[] reconstructedBinaryData = part1Bytes.Concat(part2Bytes).ToArray();
            Assert.Equal(originalBinaryData.Length, reconstructedBinaryData.Length);
            Assert.Equal(originalBinaryData, reconstructedBinaryData);

            using (var sha256 = SHA256.Create())
            {
                string reconstructedHashHex = Convert.ToHexString(sha256.ComputeHash(reconstructedBinaryData)).ToLowerInvariant();
                Assert.Equal(expectedSha256Hex, reconstructedHashHex);
            }
        }

        #endregion

        #region 5. Concurrent Streaming & Bounded Memory Allocation

        [Fact]
        public async Task PrepareDownloadAsync_ConcurrentDownloads_StreamsSimultaneouslyWithoutLocks()
        {
            using var dbContext = CreateInMemoryDbContext(Guid.NewGuid().ToString());
            var pkgRepo = new UpdatePackageRepository(dbContext);
            var relRepo = new UpdateReleaseRepository(dbContext);
            var wsRepo = new Repository<Workstation>(dbContext);
            var targetRepo = new UpdateTargetRepository(dbContext);
            var groupRepo = new WorkstationGroupRepository(dbContext);
            var storageMock = new Mock<IUpdateArtifactStorage>();

            var orgId = Guid.NewGuid();
            var workstation = new Workstation
            {
                PcId = "PC-CONCURRENT-STREAM",
                SiteId = "SITE-1",
                OrganizationEntityId = orgId,
                Hostname = "host-01",
                IpAddress = "10.0.0.23",
                MacAddress = "AA:BB:CC:DD:EE:23",
                ClientVersion = "1.0.0"
            };

            byte[] artifactBytes = new byte[256 * 1024]; // 256 KB
            new Random(123).NextBytes(artifactBytes);

            var (release, package) = CreateSignedReleaseAndPackage(orgId, "1.6.0", packageSize: artifactBytes.Length);
            var target = UpdateTarget.CreateGlobal(orgId, release.Id, 100);

            await wsRepo.AddAsync(workstation);
            await relRepo.AddAsync(release);
            await pkgRepo.AddAsync(package);
            await targetRepo.AddAsync(target);
            await dbContext.SaveChangesAsync();

            storageMock.Setup(s => s.ExistsAsync(package.StorageKey, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(true);
            storageMock.Setup(s => s.GetArtifactSizeAsync(package.StorageKey, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(artifactBytes.Length);
            storageMock.Setup(s => s.OpenReadStreamAsync(package.StorageKey, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(() => new MemoryStream(artifactBytes, writable: false));

            var eligibilityService = new UpdateEligibilityService(wsRepo, groupRepo, relRepo, targetRepo);
            var service = new UpdateDownloadService(pkgRepo, relRepo, wsRepo, eligibilityService, storageMock.Object, NullLogger<UpdateDownloadService>.Instance);

            var principal = CreateWorkstationPrincipal(orgId, workstation.PcId, workstation.Id);

            int concurrentClients = 10;
            var tasks = Enumerable.Range(0, concurrentClients).Select(async _ =>
            {
                var req = new UpdateDownloadRequest
                {
                    Principal = principal,
                    PackageId = package.Id
                };

                using var prep = await service.PrepareDownloadAsync(req);
                Assert.True(prep.IsSuccess);
                Assert.NotNull(prep.ContentStream);

                using var ms = new MemoryStream();
                await prep.ContentStream.CopyToAsync(ms);
                return ms.ToArray();
            }).ToList();

            var results = await Task.WhenAll(tasks);

            Assert.Equal(concurrentClients, results.Length);
            foreach (var downloadedData in results)
            {
                Assert.Equal(artifactBytes, downloadedData);
            }
        }

        #endregion
    }
}
