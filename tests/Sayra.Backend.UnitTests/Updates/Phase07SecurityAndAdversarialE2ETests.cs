using System;
using System.IO;
using System.Security.Cryptography;
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
    public class Phase07SecurityAndAdversarialE2ETests
    {
        private static ApplicationDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: $"E2E_Security_{Guid.NewGuid():N}")
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

        [Theory]
        [InlineData("../../../etc/passwd")]
        [InlineData("..\\..\\windows\\system32\\cmd.exe")]
        [InlineData("/etc/shadow")]
        [InlineData("C:\\boot.ini")]
        [InlineData("..%2f..%2fsecret.txt")]
        [InlineData("package\0nullbyte.spk")]
        public void PathTraversal_ValidateFilename_RejectsDangerousPaths(string maliciousFilename)
        {
            var validator = new UpdatePackageValidator(
                Options.Create(new UpdateValidationOptions()),
                NullLogger<UpdatePackageValidator>.Instance);

            Assert.Throws<InvalidDomainException>(() => validator.ValidateFilename(maliciousFilename));
        }

        [Fact]
        public async Task LocalUpdateArtifactStorage_PathTraversal_RejectsUnsafeKeys()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"sayra_storage_sec_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            try
            {
                var storage = CreateTestStorage(tempDir);

                await Assert.ThrowsAsync<InvalidDomainException>(() => storage.ExistsAsync("../../../etc/passwd"));
                await Assert.ThrowsAsync<InvalidDomainException>(() => storage.ExistsAsync("..\\..\\secret.txt"));
                await Assert.ThrowsAsync<InvalidDomainException>(() => storage.OpenReadStreamAsync("../../../etc/passwd"));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [Theory]
        [InlineData("bytes=100-50")] // Invalid range (Start > End)
        [InlineData("bytes=-10-50")] // Negative start
        [InlineData("bytes=9999999-10000000")] // Out of bounds for 1000 byte file
        [InlineData("invalid_range_syntax")]
        [InlineData("bytes=9223372036854775807-9223372036854775808")] // Overflow
        public void RangeParsing_MalformedAndOutOfBounds_HandlesSafely(string malformedRange)
        {
            long fileSize = 1000;
            var parsed = UpdateDownloadRangeHelper.ParseRangeHeader(malformedRange, fileSize);

            if (parsed.IsRangeRequest)
            {
                Assert.False(parsed.IsSatisfiable);
            }
        }

        [Fact]
        public async Task CryptographicTampering_ModifiedBinaryInStorage_TriggersTOCTOUQuarantine()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"sayra_tamper_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            try
            {
                using var dbContext = CreateDbContext();

                var releaseRepo = new UpdateReleaseRepository(dbContext);
                var packageRepo = new UpdatePackageRepository(dbContext);
                var keyRegistryRepo = new ConfigurationKeyRegistryRepository(dbContext);
                var secEventRepo = new Repository<SecurityEvent>(dbContext);

                var storage = CreateTestStorage(tempDir);
                var hashService = new UpdateHashService();
                var cryptoService = new CryptographicService();
                var secEventService = new SecurityEventService(secEventRepo, dbContext, NullLogger<SecurityEventService>.Instance);

                var privateKeyProvider = new SigningPrivateKeyProvider(Options.Create(new SecurityOptions()));
                var (keyId, pubPem, privPem) = SigningPrivateKeyProvider.GetOrCreateEphemeralKeyPair("key-tamper-01");
                privateKeyProvider.RegisterTestKeyPair(keyId, pubPem, privPem);
                await keyRegistryRepo.AddAsync(ConfigurationSigningKey.Create(keyId, pubPem, "RSA-SHA256", SigningKeyStatus.Active));
                await dbContext.SaveChangesAsync();

                var updateSigningKeyProvider = new UpdateSigningKeyProvider(keyRegistryRepo, privateKeyProvider);
                var signingService = new UpdateSigningService(updateSigningKeyProvider, cryptoService);

                var orgId = Guid.NewGuid();
                var release = UpdateRelease.Create(orgId, "v1.0.0", UpdateReleaseType.Standard);
                await releaseRepo.AddAsync(release);

                byte[] originalContent = Encoding.UTF8.GetBytes("ORIGINAL_BINARY_PAYLOAD");
                using var msOriginal = new MemoryStream(originalContent);

                string tempKey = await storage.SaveTemporaryArtifactAsync(Guid.NewGuid(), msOriginal, CancellationToken.None);
                msOriginal.Position = 0;
                string sha256Original = await hashService.ComputeSha256Async(msOriginal, CancellationToken.None);

                var package = UpdatePackage.Create(release.Id, "app.spk", originalContent.Length, tempKey, UpdatePackageType.Spk);
                package.TransitionLifecycle(UpdatePackageLifecycleState.Uploaded);
                package.TransitionLifecycle(UpdatePackageLifecycleState.Validating);
                package.TransitionLifecycle(UpdatePackageLifecycleState.Validated);
                package.SetIntegrity(sha256Original);

                string finalKey = $"packages/{release.Id:N}/{package.Id:N}.spk";
                await storage.FinalizeArtifactAsync(tempKey, finalKey, CancellationToken.None);
                package.UpdateStorageKeyAndSize(finalKey, originalContent.Length);

                // Digital Sign Package
                var signRes = await signingService.SignPackageAsync(package, keyId, CancellationToken.None);
                package.SignPackage(signRes.Signature, signRes.KeyId);

                release.AddPackage(package);
                await packageRepo.AddAsync(package);
                await dbContext.SaveChangesAsync();

                // TAMPER ATTEMPT: Overwrite storage file with malicious binary via temporary promotion!
                byte[] tamperedContent = Encoding.UTF8.GetBytes("MALICIOUS_TAMPERED_BINARY");
                using var msTampered = new MemoryStream(tamperedContent);
                string tempTamperedKey = await storage.SaveTemporaryArtifactAsync(Guid.NewGuid(), msTampered, CancellationToken.None);
                await storage.FinalizeArtifactAsync(tempTamperedKey, finalKey, CancellationToken.None);

                // Attempt prepare release -> TOCTOU integrity check MUST detect hash mismatch and quarantine package
                var authMock = new Mock<IAuthorizationService>();
                authMock.Setup(a => a.AuthorizeAsync(It.IsAny<UserPrincipal>(), It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(AuthorizationResult.Allowed());

                var prepareHandler = new PrepareUpdateReleaseCommandHandler(
                    releaseRepo, dbContext, storage, hashService, authMock.Object, secEventService);

                var adminPrincipal = new UserPrincipal { UserId = Guid.NewGuid(), OrganizationId = orgId, IsAuthenticated = true };
                var prepRes = await prepareHandler.HandleAsync(new PrepareUpdateReleaseCommand
                {
                    ReleaseId = release.Id,
                    Principal = adminPrincipal
                });

                Assert.False(prepRes.IsSuccess);
                Assert.Equal("TOCTOU_INTEGRITY_VIOLATION", prepRes.ErrorCode);

                // Package verification status MUST be Quarantined
                var updatedPackage = await packageRepo.GetByIdAsync(package.Id);
                Assert.NotNull(updatedPackage);
                Assert.Equal(UpdatePackageVerificationStatus.Quarantined, updatedPackage.VerificationStatus);
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
        public void PrivateKey_NonLeakage_NeverExposedInSerializationOrException()
        {
            var (keyId, pubPem, privPem) = SigningPrivateKeyProvider.GetOrCreateEphemeralKeyPair("key-secret-01");
            var keyContract = new ClientUpdatePackageMetadataContract
            {
                PackageId = Guid.NewGuid(),
                ReleaseId = Guid.NewGuid(),
                FileName = "app.spk",
                Size = 1000,
                ChecksumSha256 = "dummy_hash",
                Signature = "dummy_sig",
                SigningKeyId = keyId
            };

            string json = ProtocolSerialization.Serialize(keyContract);

            Assert.DoesNotContain(privPem, json);
            Assert.DoesNotContain("PRIVATE KEY", json);

            // Verify exception message formatting does not leak secret
            var domainEx = new InvalidDomainException("SECRET_ERROR", "Action failed without exposing keys.");
            Assert.DoesNotContain(privPem, domainEx.ToString());
        }
    }
}
