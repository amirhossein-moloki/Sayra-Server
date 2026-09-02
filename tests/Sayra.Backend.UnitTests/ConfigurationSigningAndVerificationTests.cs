using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Sayra.Backend.Application.Configuration;
using Sayra.Backend.Infrastructure.Persistence;
using Sayra.Backend.Infrastructure.Security;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class ConfigurationSigningAndVerificationTests
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
        public async Task SignAndVerifyPackage_ValidPayloadAndSignature_VerifiesSuccessfully()
        {
            using var dbContext = CreateInMemoryDbContext();
            var repo = new ConfigurationKeyRegistryRepository(dbContext);
            var keyProvider = new SigningPrivateKeyProvider(Microsoft.Extensions.Options.Options.Create(new Sayra.Backend.Infrastructure.Configuration.Options.SecurityOptions()));
            var keyRegistry = new ConfigurationKeyRegistry(repo, keyProvider, dbContext);

            var serializer = new CanonicalConfigurationSerializer();
            var hashService = new ConfigurationHashService(serializer);
            var cryptoService = new CryptographicService();

            var signingService = new ConfigurationSigningService(serializer, hashService, keyRegistry, keyProvider, cryptoService);

            string payload = @"{""version"":""1.0"",""server"":{""ipAddress"":""127.0.0.1"",""port"":5000}}";

            // Sign
            var signResult = await signingService.SignPackageAsync(payload);

            Assert.NotNull(signResult);
            Assert.NotEmpty(signResult.Hash);
            Assert.NotEmpty(signResult.Signature);
            Assert.Equal("RSA-SHA256", signResult.Algorithm);
            Assert.Equal("config-signing-active-01", signResult.KeyId);

            // Verify
            var verifyResult = await signingService.VerifyPackageAsync(payload, signResult.Hash, signResult.Signature, signResult.KeyId);

            Assert.True(verifyResult.IsValid, verifyResult.FailureReason);
            Assert.Equal(signResult.KeyId, verifyResult.KeyId);
        }

        [Fact]
        public async Task VerifyPackage_TamperedPayload_FailsVerification()
        {
            using var dbContext = CreateInMemoryDbContext();
            var repo = new ConfigurationKeyRegistryRepository(dbContext);
            var keyProvider = new SigningPrivateKeyProvider(Microsoft.Extensions.Options.Options.Create(new Sayra.Backend.Infrastructure.Configuration.Options.SecurityOptions()));
            var keyRegistry = new ConfigurationKeyRegistry(repo, keyProvider, dbContext);

            var serializer = new CanonicalConfigurationSerializer();
            var hashService = new ConfigurationHashService(serializer);
            var cryptoService = new CryptographicService();

            var signingService = new ConfigurationSigningService(serializer, hashService, keyRegistry, keyProvider, cryptoService);

            string originalPayload = @"{""server"":{""port"":5000}}";
            var signResult = await signingService.SignPackageAsync(originalPayload);

            string tamperedPayload = @"{""server"":{""port"":5001}}";

            var verifyResult = await signingService.VerifyPackageAsync(tamperedPayload, signResult.Hash, signResult.Signature, signResult.KeyId);

            Assert.False(verifyResult.IsValid);
            Assert.Contains("hash", verifyResult.FailureReason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task VerifyPackage_TamperedHash_FailsVerification()
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
            var signResult = await signingService.SignPackageAsync(payload);

            string tamperedHash = "0000000000000000000000000000000000000000000000000000000000000000";

            var verifyResult = await signingService.VerifyPackageAsync(payload, tamperedHash, signResult.Signature, signResult.KeyId);

            Assert.False(verifyResult.IsValid);
        }

        [Fact]
        public async Task VerifyPackage_UnknownKeyId_FailsVerification()
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
            var signResult = await signingService.SignPackageAsync(payload);

            var verifyResult = await signingService.VerifyPackageAsync(payload, signResult.Hash, signResult.Signature, "unknown-key-999");

            Assert.False(verifyResult.IsValid);
            Assert.Contains("Unknown KeyId", verifyResult.FailureReason);
        }
    }
}
