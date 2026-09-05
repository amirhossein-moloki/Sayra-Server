using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
using Sayra.Backend.Infrastructure.Security;
using Sayra.Backend.Infrastructure.Updates;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class UpdateSigningAndVerificationUnitTests
    {
        private static ApplicationDbContext CreateInMemoryDbContext(string dbName)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .Options;
            return new ApplicationDbContext(options);
        }

        #region 1. Canonicalization Tests

        [Fact]
        public void Canonicalization_BuildCanonicalPayloadString_ProducesDeterministicFormat()
        {
            var pkgId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var relId = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var package = UpdatePackage.Create(relId, "  Update.spk  ", 1048576, "storage/update.spk", UpdatePackageType.Spk);

            // set package ID using reflection or domain helper
            typeof(BaseEntity).GetProperty("Id")!.SetValue(package, pkgId);
            package.SetIntegrity("E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855");

            string canonical = UpdateSigningCanonicalizer.BuildCanonicalPayloadString(package);

            Assert.Equal("11111111-1111-1111-1111-111111111111:22222222-2222-2222-2222-222222222222:Update.spk:1048576:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", canonical);
        }

        [Fact]
        public void Canonicalization_WithoutHash_ThrowsInvalidDomainException()
        {
            var relId = Guid.NewGuid();
            var package = UpdatePackage.Create(relId, "app.spk", 1024, "storage/app.spk");

            Assert.Throws<InvalidDomainException>(() => UpdateSigningCanonicalizer.BuildCanonicalPayloadString(package));
        }

        #endregion

        #region 2. Signing & Verification Core Pipeline Tests

        [Fact]
        public async Task SigningAndVerification_ValidPackage_SucceedsAndMatchesClientContract()
        {
            using var dbContext = CreateInMemoryDbContext(Guid.NewGuid().ToString());
            var keyRegistryRepo = new ConfigurationKeyRegistryRepository(dbContext);
            var packageRepo = new UpdatePackageRepository(dbContext);
            var releaseRepo = new UpdateReleaseRepository(dbContext);

            var (keyId, pubPem, privPem) = SigningPrivateKeyProvider.GetOrCreateEphemeralKeyPair("key-test-sign-01");
            var privateKeyProvider = new SigningPrivateKeyProvider(Options.Create(new SecurityOptions()));
            privateKeyProvider.RegisterTestKeyPair(keyId, pubPem, privPem);

            var keyRegistry = new UpdateSigningKeyProvider(keyRegistryRepo, privateKeyProvider);
            await keyRegistryRepo.AddAsync(ConfigurationSigningKey.Create(keyId, pubPem, "RSA-SHA256", SigningKeyStatus.Active));
            await dbContext.SaveChangesAsync();

            var cryptoService = new CryptographicService();
            var signingService = new UpdateSigningService(keyRegistry, cryptoService);

            var orgId = Guid.NewGuid();
            var release = UpdateRelease.Create(orgId, "1.0.0", UpdateReleaseType.Standard);
            var package = UpdatePackage.Create(release.Id, "client-v1.spk", 2048576, "packages/v1.spk", UpdatePackageType.Spk);

            package.TransitionLifecycle(UpdatePackageLifecycleState.Uploaded);
            package.TransitionLifecycle(UpdatePackageLifecycleState.Validating);
            package.TransitionLifecycle(UpdatePackageLifecycleState.Validated);
            package.SetIntegrity("a591a6d40bf420404a011733cfb7b190d62c65bf0bcda32b57b277d9ad9f146e");

            var signResult = await signingService.SignPackageAsync(package, keyId);

            Assert.NotNull(signResult);
            Assert.Equal("a591a6d40bf420404a011733cfb7b190d62c65bf0bcda32b57b277d9ad9f146e", signResult.Hash);
            Assert.Equal(keyId, signResult.KeyId);
            Assert.False(string.IsNullOrWhiteSpace(signResult.Signature));

            package.SignPackage(signResult.Signature, signResult.KeyId);

            // Verify using signing service
            var verifyResult = await signingService.VerifyPackageAsync(package);
            Assert.True(verifyResult.IsValid);
            Assert.Equal(keyId, verifyResult.KeyId);

            // Directly verify using raw RSA against public key
            byte[] canonicalBytes = UpdateSigningCanonicalizer.BuildCanonicalPayloadBytes(package);
            byte[] sigBytes = Convert.FromBase64String(package.Signature!);
            bool directRsaValid = cryptoService.VerifyDataRsa(canonicalBytes, sigBytes, pubPem);
            Assert.True(directRsaValid);
        }

        #endregion

        #region 3. Tamper Detection Tests

        [Theory]
        [InlineData("FileName", "tampered.spk")]
        [InlineData("Size", 999999L)]
        [InlineData("SHA256", "0000000000000000000000000000000000000000000000000000000000000000")]
        public async Task Verification_TamperedPayloadProperty_FailsVerification(string propertyName, object tamperedValue)
        {
            using var dbContext = CreateInMemoryDbContext(Guid.NewGuid().ToString());
            var keyRegistryRepo = new ConfigurationKeyRegistryRepository(dbContext);

            var (keyId, pubPem, privPem) = SigningPrivateKeyProvider.GetOrCreateEphemeralKeyPair("key-tamper-01");
            var privateKeyProvider = new SigningPrivateKeyProvider(Options.Create(new SecurityOptions()));
            privateKeyProvider.RegisterTestKeyPair(keyId, pubPem, privPem);

            var keyRegistry = new UpdateSigningKeyProvider(keyRegistryRepo, privateKeyProvider);
            await keyRegistryRepo.AddAsync(ConfigurationSigningKey.Create(keyId, pubPem, "RSA-SHA256", SigningKeyStatus.Active));
            await dbContext.SaveChangesAsync();

            var cryptoService = new CryptographicService();
            var signingService = new UpdateSigningService(keyRegistry, cryptoService);

            var releaseId = Guid.NewGuid();
            var package = UpdatePackage.Create(releaseId, "app.spk", 1048576, "storage/app.spk");
            package.TransitionLifecycle(UpdatePackageLifecycleState.Uploaded);
            package.TransitionLifecycle(UpdatePackageLifecycleState.Validating);
            package.TransitionLifecycle(UpdatePackageLifecycleState.Validated);
            package.SetIntegrity("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");

            var signResult = await signingService.SignPackageAsync(package, keyId);
            package.SignPackage(signResult.Signature, signResult.KeyId);

            // Mutate property via reflection to simulate tampering after signing
            if (propertyName == "FileName")
            {
                typeof(UpdatePackage).GetProperty("FileName")!.SetValue(package, (string)tamperedValue);
            }
            else if (propertyName == "Size")
            {
                typeof(UpdatePackage).GetProperty("Size")!.SetValue(package, (long)tamperedValue);
            }
            else if (propertyName == "SHA256")
            {
                typeof(UpdatePackage).GetProperty("SHA256")!.SetValue(package, (string)tamperedValue);
            }

            var verifyResult = await signingService.VerifyPackageAsync(package);
            Assert.False(verifyResult.IsValid);
        }

        #endregion

        #region 4. TOCTOU Protection & Handler Tests

        [Fact]
        public async Task Handler_TOCTOU_ArtifactModifiedInStorage_QuarantinesPackageAndRejects()
        {
            using var dbContext = CreateInMemoryDbContext(Guid.NewGuid().ToString());
            var packageRepo = new UpdatePackageRepository(dbContext);
            var releaseRepo = new UpdateReleaseRepository(dbContext);

            var (keyId, pubPem, privPem) = SigningPrivateKeyProvider.GetOrCreateEphemeralKeyPair("key-toctou-01");
            var privateKeyProvider = new SigningPrivateKeyProvider(Options.Create(new SecurityOptions()));
            privateKeyProvider.RegisterTestKeyPair(keyId, pubPem, privPem);

            var keyRegistryRepo = new ConfigurationKeyRegistryRepository(dbContext);
            await keyRegistryRepo.AddAsync(ConfigurationSigningKey.Create(keyId, pubPem, "RSA-SHA256", SigningKeyStatus.Active));

            var keyRegistry = new UpdateSigningKeyProvider(keyRegistryRepo, privateKeyProvider);
            var cryptoService = new CryptographicService();
            var signingService = new UpdateSigningService(keyRegistry, cryptoService);

            var storageMock = new Mock<IUpdateArtifactStorage>();
            var hashServiceMock = new Mock<IUpdateHashService>();
            var authMock = new Mock<IAuthorizationService>();
            var auditMock = new Mock<ISecurityEventService>();

            var orgId = Guid.NewGuid();
            var release = UpdateRelease.Create(orgId, "1.0.0", UpdateReleaseType.Standard);
            var package = UpdatePackage.Create(release.Id, "game-update.spk", 5000, "packages/game.spk");

            package.TransitionLifecycle(UpdatePackageLifecycleState.Uploaded);
            package.TransitionLifecycle(UpdatePackageLifecycleState.Validating);
            package.TransitionLifecycle(UpdatePackageLifecycleState.Validated);
            package.SetIntegrity("1111111111111111111111111111111111111111111111111111111111111111");

            await releaseRepo.AddAsync(release);
            await packageRepo.AddAsync(package);
            await dbContext.SaveChangesAsync();

            // Mock storage opening read stream
            var dummyStream = new MemoryStream(Encoding.UTF8.GetBytes("modified content stream"));
            storageMock.Setup(s => s.OpenReadStreamAsync(package.StorageKey, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(dummyStream);

            // Hash service calculates DIFFERENT hash from package.SHA256
            hashServiceMock.Setup(h => h.ComputeSha256Async(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                           .ReturnsAsync("2222222222222222222222222222222222222222222222222222222222222222");

            authMock.Setup(a => a.AuthorizeAsync(It.IsAny<UserPrincipal>(), It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(AuthorizationResult.Allowed());

            var handler = new SignUpdatePackageCommandHandler(
                packageRepo, releaseRepo, dbContext, storageMock.Object, hashServiceMock.Object,
                signingService, authMock.Object, auditMock.Object);

            var userId = Guid.NewGuid();
            var principal = new UserPrincipal
            {
                UserId = userId,
                Username = "admin",
                OrganizationId = orgId,
                Permissions = new System.Collections.Generic.List<string> { "ManageUpdates" },
                IsAuthenticated = true
            };

            var command = new SignUpdatePackageCommand
            {
                PackageId = package.Id,
                KeyId = keyId,
                Principal = principal
            };

            var result = await handler.HandleAsync(command);

            Assert.False(result.IsSuccess);
            Assert.Equal("TOCTOU_INTEGRITY_VIOLATION", result.ErrorCode);

            // Verify package was quarantined in DB
            var updatedPkg = await packageRepo.GetByIdAsync(package.Id);
            Assert.NotNull(updatedPkg);
            Assert.Equal(UpdatePackageLifecycleState.Quarantined, updatedPkg.LifecycleState);
            Assert.Equal(UpdatePackageVerificationStatus.Quarantined, updatedPkg.VerificationStatus);
        }

        #endregion

        #region 5. Lifecycle Precondition Tests

        [Fact]
        public void SignPackage_UnvalidatedPackage_ThrowsInvalidDomainException()
        {
            var relId = Guid.NewGuid();
            var package = UpdatePackage.Create(relId, "raw.spk", 100, "storage/raw.spk");

            Assert.Throws<InvalidDomainException>(() => package.SignPackage("DummySig", "key-1"));
        }

        #endregion

        #region 6. Key Rotation Tests

        [Fact]
        public async Task KeyRotation_RetiredKey_StillVerifiesHistoricalRelease_ButCannotSignNewPackage()
        {
            using var dbContext = CreateInMemoryDbContext(Guid.NewGuid().ToString());
            var keyRegistryRepo = new ConfigurationKeyRegistryRepository(dbContext);

            var (keyA, pubPemA, privPemA) = SigningPrivateKeyProvider.GetOrCreateEphemeralKeyPair("key-rot-A");
            var (keyB, pubPemB, privPemB) = SigningPrivateKeyProvider.GetOrCreateEphemeralKeyPair("key-rot-B");

            var privateKeyProvider = new SigningPrivateKeyProvider(Options.Create(new SecurityOptions()));
            privateKeyProvider.RegisterTestKeyPair(keyA, pubPemA, privPemA);
            privateKeyProvider.RegisterTestKeyPair(keyB, pubPemB, privPemB);

            // Key A active initially
            var keyEntityA = ConfigurationSigningKey.Create(keyA, pubPemA, "RSA-SHA256", SigningKeyStatus.Active);
            await keyRegistryRepo.AddAsync(keyEntityA);
            await dbContext.SaveChangesAsync();

            var keyRegistry = new UpdateSigningKeyProvider(keyRegistryRepo, privateKeyProvider);
            var cryptoService = new CryptographicService();
            var signingService = new UpdateSigningService(keyRegistry, cryptoService);

            // Package 1 signed by Key A
            var relId = Guid.NewGuid();
            var package1 = UpdatePackage.Create(relId, "pkg1.spk", 1000, "storage/pkg1.spk");
            package1.TransitionLifecycle(UpdatePackageLifecycleState.Uploaded);
            package1.TransitionLifecycle(UpdatePackageLifecycleState.Validating);
            package1.TransitionLifecycle(UpdatePackageLifecycleState.Validated);
            package1.SetIntegrity("a591a6d40bf420404a011733cfb7b190d62c65bf0bcda32b57b277d9ad9f146e");

            var sig1 = await signingService.SignPackageAsync(package1, keyA);
            package1.SignPackage(sig1.Signature, sig1.KeyId);

            // Key Rotation: Key A retired, Key B active
            keyEntityA.Retire();
            var keyEntityB = ConfigurationSigningKey.Create(keyB, pubPemB, "RSA-SHA256", SigningKeyStatus.Active);
            await keyRegistryRepo.AddAsync(keyEntityB);
            await dbContext.SaveChangesAsync();

            // 1. Key A can still verify historical package1
            var verifyResultPkg1 = await signingService.VerifyPackageAsync(package1);
            Assert.True(verifyResultPkg1.IsValid);
            Assert.Equal(keyA, verifyResultPkg1.KeyId);

            // 2. Retired Key A cannot sign new package2
            var package2 = UpdatePackage.Create(relId, "pkg2.spk", 2000, "storage/pkg2.spk");
            package2.TransitionLifecycle(UpdatePackageLifecycleState.Uploaded);
            package2.TransitionLifecycle(UpdatePackageLifecycleState.Validating);
            package2.TransitionLifecycle(UpdatePackageLifecycleState.Validated);
            package2.SetIntegrity("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");

            await Assert.ThrowsAsync<InvalidDomainException>(() => signingService.SignPackageAsync(package2, keyA));

            // 3. Active Key B signs package2 successfully
            var sig2 = await signingService.SignPackageAsync(package2, keyB);
            package2.SignPackage(sig2.Signature, sig2.KeyId);

            var verifyResultPkg2 = await signingService.VerifyPackageAsync(package2);
            Assert.True(verifyResultPkg2.IsValid);
            Assert.Equal(keyB, verifyResultPkg2.KeyId);
        }

        #endregion
    }
}
