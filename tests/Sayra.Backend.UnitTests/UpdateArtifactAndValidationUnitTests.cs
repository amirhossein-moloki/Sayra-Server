using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Security;
using Sayra.Backend.Application.Updates;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Exceptions;
using Sayra.Backend.Infrastructure.Configuration.Options;
using Sayra.Backend.Infrastructure.Persistence;
using Sayra.Backend.Infrastructure.Updates;
using Xunit;

#nullable enable

namespace Sayra.Backend.UnitTests
{
    public class UpdateArtifactAndValidationUnitTests : IDisposable
    {
        private readonly string _testStoragePath;

        public UpdateArtifactAndValidationUnitTests()
        {
            _testStoragePath = Path.Combine(Path.GetTempPath(), $"SayraUpdateTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_testStoragePath);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testStoragePath))
                {
                    Directory.Delete(_testStoragePath, recursive: true);
                }
            }
            catch
            {
                // Ignore cleanup errors in temporary directories
            }
        }

        #region Helper Methods

        private LocalUpdateArtifactStorage CreateLocalStorageProvider()
        {
            var options = Options.Create(new UpdatesOptions
            {
                LocalUpdateRepositoryPath = _testStoragePath
            });

            return new LocalUpdateArtifactStorage(options, NullLogger<LocalUpdateArtifactStorage>.Instance);
        }

        private UpdatePackageValidator CreatePackageValidator(long maxSizeBytes = 524_288_000)
        {
            var options = Options.Create(new UpdateValidationOptions
            {
                MaxArtifactSizeBytes = maxSizeBytes
            });

            return new UpdatePackageValidator(options, NullLogger<UpdatePackageValidator>.Instance);
        }

        private static byte[] CreateValidZipPackageBytes(string entryName = "app.bin", string content = "Package payload content")
        {
            using var ms = new MemoryStream();
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                var entry = archive.CreateEntry(entryName);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
            return ms.ToArray();
        }

        private static byte[] CreateMaliciousZipPackageBytes(string maliciousPath = "../unsafe.txt")
        {
            using var ms = new MemoryStream();
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                var entry = archive.CreateEntry(maliciousPath);
                using var writer = new StreamWriter(entry.Open());
                writer.Write("Malicious file content");
            }
            return ms.ToArray();
        }

        private ApplicationDbContext CreateInMemoryDbContext()
        {
            var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: $"UpdateArtifactTestDb_{Guid.NewGuid():N}")
                .Options;

            return new ApplicationDbContext(dbOptions);
        }

        private static UserPrincipal CreateAdminPrincipal(Guid orgId, Guid? userId = null)
        {
            return new UserPrincipal
            {
                UserId = userId ?? Guid.NewGuid(),
                Username = "admin",
                OrganizationId = orgId,
                Roles = new List<string> { "Administrator" },
                Permissions = new List<string> { PermissionCatalog.ManageUpdates, PermissionCatalog.ViewUpdates },
                IsAuthenticated = true
            };
        }

        #endregion

        #region Storage Provider Unit Tests

        [Fact]
        public async Task LocalStorage_SaveTemporaryAndFinalize_SucceedsWithIsolation()
        {
            var storage = CreateLocalStorageProvider();
            var packageId = Guid.NewGuid();
            var payload = Encoding.UTF8.GetBytes("Test artifact payload content");
            using var sourceStream = new MemoryStream(payload);

            var tempKey = await storage.SaveTemporaryArtifactAsync(packageId, sourceStream);

            Assert.True(await storage.ExistsAsync(tempKey));
            Assert.Contains("temp/", tempKey);

            var finalKey = $"packages/r1/{packageId:N}.spk";
            await storage.FinalizeArtifactAsync(tempKey, finalKey);

            Assert.False(await storage.ExistsAsync(tempKey));
            Assert.True(await storage.ExistsAsync(finalKey));

            using var readStream = await storage.OpenReadStreamAsync(finalKey);
            using var ms = new MemoryStream();
            await readStream.CopyToAsync(ms);

            Assert.Equal(payload, ms.ToArray());
        }

        [Theory]
        [InlineData("../unsafe.spk")]
        [InlineData("temp/../../etc/passwd")]
        [InlineData("packages/..\\..\\windows\\system32\\cmd.exe")]
        public async Task LocalStorage_PathTraversalKey_ThrowsInvalidDomainException(string unsafeKey)
        {
            var storage = CreateLocalStorageProvider();

            await Assert.ThrowsAsync<InvalidDomainException>(() => storage.ExistsAsync(unsafeKey));
            await Assert.ThrowsAsync<InvalidDomainException>(() => storage.OpenReadStreamAsync(unsafeKey));
            await Assert.ThrowsAsync<InvalidDomainException>(() => storage.FinalizeArtifactAsync("temp/valid.tmp", unsafeKey));
        }

        #endregion

        #region Structural & Metadata Validator Tests

        [Fact]
        public void PackageValidator_FilenameValidation_ValidatesCorrectly()
        {
            var validator = CreatePackageValidator();

            validator.ValidateFilename("update-v1.0.0.spk");

            Assert.Throws<InvalidDomainException>(() => validator.ValidateFilename(""));
            Assert.Throws<InvalidDomainException>(() => validator.ValidateFilename("   "));
            Assert.Throws<InvalidDomainException>(() => validator.ValidateFilename("update/traversal.spk"));
            Assert.Throws<InvalidDomainException>(() => validator.ValidateFilename("update..spk"));
            Assert.Throws<InvalidDomainException>(() => validator.ValidateFilename("update.exe.spk"));
            Assert.Throws<InvalidDomainException>(() => validator.ValidateFilename("update.bat.zip"));
        }

        [Fact]
        public void PackageValidator_SizeValidation_EnforcesLimits()
        {
            var validator = CreatePackageValidator(maxSizeBytes: 1024);

            validator.ValidateSize(500);

            Assert.Throws<InvalidDomainException>(() => validator.ValidateSize(0));
            Assert.Throws<InvalidDomainException>(() => validator.ValidateSize(-10));
            Assert.Throws<InvalidDomainException>(() => validator.ValidateSize(2000));
        }

        [Fact]
        public async Task PackageValidator_ValidZipContainer_ReturnsSuccess()
        {
            var validator = CreatePackageValidator();
            var zipBytes = CreateValidZipPackageBytes();
            using var stream = new MemoryStream(zipBytes);

            var result = await validator.ValidateStructureAsync(stream, UpdatePackageType.Spk);

            Assert.True(result.IsSuccess);
            Assert.False(result.IsQuarantined);
        }

        [Fact]
        public async Task PackageValidator_ZipPathTraversal_TriggersQuarantine()
        {
            var validator = CreatePackageValidator();
            var maliciousZipBytes = CreateMaliciousZipPackageBytes("../etc/shadow");
            using var stream = new MemoryStream(maliciousZipBytes);

            var result = await validator.ValidateStructureAsync(stream, UpdatePackageType.Spk);

            Assert.False(result.IsSuccess);
            Assert.True(result.IsQuarantined);
            Assert.Equal("SECURITY_QUARANTINE_TRAVERSAL", result.ErrorCode);
        }

        #endregion

        #region Hash Service Tests

        [Fact]
        public async Task HashService_ComputeAndValidateDeclaredHash_Succeeds()
        {
            var hashService = new UpdateHashService();
            var payload = Encoding.UTF8.GetBytes("Exact package payload for hash test");
            using var stream = new MemoryStream(payload);

            var calculatedSha = await hashService.ComputeSha256Async(stream);

            using var sha256Alg = SHA256.Create();
            var expectedSha = Convert.ToHexString(sha256Alg.ComputeHash(payload)).ToLowerInvariant();

            Assert.Equal(expectedSha, calculatedSha);

            hashService.ValidateDeclaredHash(calculatedSha, expectedSha);

            Assert.Throws<InvalidDomainException>(() => hashService.ValidateDeclaredHash(calculatedSha, new string('b', 64)));
        }

        #endregion

        #region Application Upload Command Handlers & Integration Tests

        [Fact]
        public async Task UploadUpdatePackage_ValidPackage_UploadsValidatesAndPromotes()
        {
            using var context = CreateInMemoryDbContext();
            var releaseRepo = new UpdateReleaseRepository(context);
            var packageRepo = new UpdatePackageRepository(context);
            var unitOfWork = context;
            var storage = CreateLocalStorageProvider();
            var validator = CreatePackageValidator();
            var hashService = new UpdateHashService();

            var orgId = Guid.NewGuid();
            var release = UpdateRelease.Create(orgId, "v2.5.0", UpdateReleaseType.Standard, "Release notes", "admin");
            await releaseRepo.AddAsync(release);
            await context.SaveChangesAsync();

            var principal = CreateAdminPrincipal(orgId);

            var mockAuthService = new Mock<IAuthorizationService>();
            mockAuthService.Setup(a => a.AuthorizeAsync(principal, PermissionCatalog.ManageUpdates, It.IsAny<object>(), It.IsAny<CancellationToken>()))
                           .ReturnsAsync(AuthorizationResult.Allowed());

            var mockSecurityEventService = new Mock<ISecurityEventService>();

            var handler = new UploadUpdatePackageCommandHandler(
                releaseRepo,
                packageRepo,
                unitOfWork,
                storage,
                validator,
                hashService,
                mockAuthService.Object,
                mockSecurityEventService.Object,
                NullLogger<UploadUpdatePackageCommandHandler>.Instance);

            var zipBytes = CreateValidZipPackageBytes("payload.dll", "Binary data content");
            using var contentStream = new MemoryStream(zipBytes);

            var command = new UploadUpdatePackageCommand
            {
                ReleaseId = release.Id,
                FileName = "package-v2.5.0.spk",
                ContentStream = contentStream,
                PackageType = UpdatePackageType.Spk,
                Principal = principal
            };

            var result = await handler.HandleAsync(command, CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal(release.Id, result.Value!.ReleaseId);
            Assert.Equal("package-v2.5.0.spk", result.Value.FileName);
            Assert.Equal(zipBytes.Length, result.Value.Size);
            Assert.False(string.IsNullOrWhiteSpace(result.Value.ChecksumSha256));

            var persistedPackage = await packageRepo.GetByIdAsync(result.Value.PackageId);
            Assert.NotNull(persistedPackage);
            Assert.Equal(UpdatePackageLifecycleState.Validated, persistedPackage!.LifecycleState);
            Assert.Equal(UpdatePackageVerificationStatus.Valid, persistedPackage.VerificationStatus);
            Assert.True(await storage.ExistsAsync(persistedPackage.StorageKey));
        }

        [Fact]
        public async Task UploadUpdatePackage_CrossOrganization_RejectsAccess()
        {
            using var context = CreateInMemoryDbContext();
            var releaseRepo = new UpdateReleaseRepository(context);
            var packageRepo = new UpdatePackageRepository(context);
            var storage = CreateLocalStorageProvider();
            var validator = CreatePackageValidator();
            var hashService = new UpdateHashService();

            var orgA = Guid.NewGuid();
            var orgB = Guid.NewGuid();

            var releaseOrgA = UpdateRelease.Create(orgA, "v1.0.0");
            await releaseRepo.AddAsync(releaseOrgA);
            await context.SaveChangesAsync();

            var principalOrgB = CreateAdminPrincipal(orgB);

            var mockAuthService = new Mock<IAuthorizationService>();
            mockAuthService.Setup(a => a.AuthorizeAsync(principalOrgB, PermissionCatalog.ManageUpdates, It.IsAny<object>(), It.IsAny<CancellationToken>()))
                           .ReturnsAsync(AuthorizationResult.Allowed());

            var handler = new UploadUpdatePackageCommandHandler(
                releaseRepo,
                packageRepo,
                context,
                storage,
                validator,
                hashService,
                mockAuthService.Object,
                Mock.Of<ISecurityEventService>(),
                NullLogger<UploadUpdatePackageCommandHandler>.Instance);

            var zipBytes = CreateValidZipPackageBytes();
            using var stream = new MemoryStream(zipBytes);

            var command = new UploadUpdatePackageCommand
            {
                ReleaseId = releaseOrgA.Id,
                FileName = "cross-org-pkg.spk",
                ContentStream = stream,
                Principal = principalOrgB
            };

            var result = await handler.HandleAsync(command, CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal("CROSS_ORGANIZATION_ACCESS_DENIED", result.ErrorCode);
        }

        [Fact]
        public async Task UploadUpdatePackage_DeclaredHashMismatch_RejectsAndCleansTempFile()
        {
            using var context = CreateInMemoryDbContext();
            var releaseRepo = new UpdateReleaseRepository(context);
            var packageRepo = new UpdatePackageRepository(context);
            var storage = CreateLocalStorageProvider();
            var validator = CreatePackageValidator();
            var hashService = new UpdateHashService();

            var orgId = Guid.NewGuid();
            var release = UpdateRelease.Create(orgId, "v3.0.0");
            await releaseRepo.AddAsync(release);
            await context.SaveChangesAsync();

            var principal = CreateAdminPrincipal(orgId);
            var mockAuthService = new Mock<IAuthorizationService>();
            mockAuthService.Setup(a => a.AuthorizeAsync(principal, PermissionCatalog.ManageUpdates, It.IsAny<object>(), It.IsAny<CancellationToken>()))
                           .ReturnsAsync(AuthorizationResult.Allowed());

            var handler = new UploadUpdatePackageCommandHandler(
                releaseRepo,
                packageRepo,
                context,
                storage,
                validator,
                hashService,
                mockAuthService.Object,
                Mock.Of<ISecurityEventService>(),
                NullLogger<UploadUpdatePackageCommandHandler>.Instance);

            var zipBytes = CreateValidZipPackageBytes();
            using var stream = new MemoryStream(zipBytes);

            var wrongDeclaredHash = new string('f', 64);

            var command = new UploadUpdatePackageCommand
            {
                ReleaseId = release.Id,
                FileName = "mismatch.spk",
                ContentStream = stream,
                DeclaredSha256 = wrongDeclaredHash,
                Principal = principal
            };

            var result = await handler.HandleAsync(command, CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal("HASH_MISMATCH", result.ErrorCode);
        }

        [Fact]
        public async Task LargeArtifactStreaming_ProcessesInChunksWithoutError()
        {
            var storage = CreateLocalStorageProvider();
            var hashService = new UpdateHashService();
            var validator = CreatePackageValidator(maxSizeBytes: 20_000_000);

            // Generate 10MB simulated stream
            var totalSize = 10 * 1024 * 1024;
            using var largeStream = new NonSeekableChunkStream(totalSize);

            var packageId = Guid.NewGuid();
            var tempKey = await storage.SaveTemporaryArtifactAsync(packageId, largeStream);

            Assert.True(await storage.ExistsAsync(tempKey));

            using var readStream = await storage.OpenReadStreamAsync(tempKey);
            var calculatedSha256 = await hashService.ComputeSha256Async(readStream);
            var calculatedSize = await storage.GetArtifactSizeAsync(tempKey);

            Assert.Equal(totalSize, calculatedSize);
            Assert.Equal(64, calculatedSha256.Length);

            validator.ValidateSize(calculatedSize);
        }

        #endregion

        #region Helper Stream Class

        private class NonSeekableChunkStream : Stream
        {
            private readonly long _length;
            private long _position;

            public NonSeekableChunkStream(long length)
            {
                _length = length;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => _length;
            public override long Position
            {
                get => _position;
                set => throw new NotSupportedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_position >= _length) return 0;

                int toRead = (int)Math.Min(count, _length - _position);
                for (int i = 0; i < toRead; i++)
                {
                    buffer[offset + i] = (byte)((_position + i) % 256);
                }

                _position += toRead;
                return toRead;
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                return Task.FromResult(Read(buffer, offset, count));
            }

            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        #endregion
    }
}
