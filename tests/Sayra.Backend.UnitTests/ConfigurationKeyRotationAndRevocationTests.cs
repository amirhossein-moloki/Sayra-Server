using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Sayra.Backend.Application.Configuration;
using Sayra.Backend.Infrastructure.Persistence;
using Sayra.Backend.Infrastructure.Security;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class ConfigurationKeyRotationAndRevocationTests
    {
        private static ApplicationDbContext CreateInMemoryDbContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            return new ApplicationDbContext(options);
        }

        [Fact]
        public async Task KeyRotation_RotatesActiveKey_HistoricalSignaturesVerifiableAndNewKeySignsNewPackages()
        {
            using var dbContext = CreateInMemoryDbContext();
            var repo = new ConfigurationKeyRegistryRepository(dbContext);
            var keyProvider = new SigningPrivateKeyProvider(Microsoft.Extensions.Options.Options.Create(new Sayra.Backend.Infrastructure.Configuration.Options.SecurityOptions()));
            var keyRegistry = new ConfigurationKeyRegistry(repo, keyProvider, dbContext);

            var serializer = new CanonicalConfigurationSerializer();
            var hashService = new ConfigurationHashService(serializer);
            var cryptoService = new CryptographicService();
            var signingService = new ConfigurationSigningService(serializer, hashService, keyRegistry, keyProvider, cryptoService);

            // 1. Sign v1 with Key A (default active key)
            string payload1 = @"{""version"":""1.0"",""server"":{""port"":5000}}";
            var signRes1 = await signingService.SignPackageAsync(payload1);
            Assert.Equal("config-signing-active-01", signRes1.KeyId);

            // 2. Generate Key B and rotate
            using var rsaB = RSA.Create(2048);
            string keyIdB = "config-signing-2026-02";
            string pubKeyB = rsaB.ExportSubjectPublicKeyInfoPem();
            string privKeyB = rsaB.ExportPkcs8PrivateKeyPem();

            keyProvider.RegisterTestKeyPair(keyIdB, pubKeyB, privKeyB);
            bool rotationSuccess = await keyRegistry.RotateActiveKeyAsync(keyIdB, pubKeyB);
            Assert.True(rotationSuccess);

            // 3. Sign v2 with newly active Key B
            string payload2 = @"{""version"":""2.0"",""server"":{""port"":5001}}";
            var signRes2 = await signingService.SignPackageAsync(payload2);
            Assert.Equal(keyIdB, signRes2.KeyId);

            // 4. Verify v1 (signed with retired Key A) is still valid for historical verification
            var verifyRes1 = await signingService.VerifyPackageAsync(payload1, signRes1.Hash, signRes1.Signature, signRes1.KeyId);
            Assert.True(verifyRes1.IsValid, verifyRes1.FailureReason);

            // 5. Verify v2 (signed with active Key B) is valid
            var verifyRes2 = await signingService.VerifyPackageAsync(payload2, signRes2.Hash, signRes2.Signature, signRes2.KeyId);
            Assert.True(verifyRes2.IsValid, verifyRes2.FailureReason);
        }

        [Fact]
        public async Task KeyRevocation_RevokedKey_CannotSignNewPackagesAndVerificationFails()
        {
            using var dbContext = CreateInMemoryDbContext();
            var repo = new ConfigurationKeyRegistryRepository(dbContext);
            var keyProvider = new SigningPrivateKeyProvider(Microsoft.Extensions.Options.Options.Create(new Sayra.Backend.Infrastructure.Configuration.Options.SecurityOptions()));
            var keyRegistry = new ConfigurationKeyRegistry(repo, keyProvider, dbContext);

            var serializer = new CanonicalConfigurationSerializer();
            var hashService = new ConfigurationHashService(serializer);
            var cryptoService = new CryptographicService();
            var signingService = new ConfigurationSigningService(serializer, hashService, keyRegistry, keyProvider, cryptoService);

            string payload = @"{""server"":{""port"":5000}}";
            var signRes = await signingService.SignPackageAsync(payload);

            // Revoke Key
            bool revokeSuccess = await keyRegistry.RevokeKeyAsync(signRes.KeyId);
            Assert.True(revokeSuccess);

            // 1. Attempting to sign new package with revoked key fails
            await Assert.ThrowsAsync<InvalidOperationException>(() => signingService.SignPackageAsync(@"{""server"":{""port"":5001}}", keyId: signRes.KeyId));

            // 2. Verification of package signed with revoked key fails
            var verifyResult = await signingService.VerifyPackageAsync(payload, signRes.Hash, signRes.Signature, signRes.KeyId);
            Assert.False(verifyResult.IsValid);
            Assert.Contains("revoked", verifyResult.FailureReason);
        }
    }
}
