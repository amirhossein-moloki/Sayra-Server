using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Sayra.Backend.Application.Configuration;
using Sayra.Backend.Domain;
using Sayra.Backend.Infrastructure.Persistence;
using Sayra.Backend.Infrastructure.Security;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class ConfigurationCryptographicMetadataTests
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
        public void ConfigurationPackage_SetCryptographicSignature_EnforcesImmutabilityOnceSigned()
        {
            var package = ConfigurationPackage.CreateFull("default", 1, @"{""server"":{""port"":5000}}");

            package.SetCryptographicSignature(
                hash: "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789",
                signature: "Base64SignatureValue==",
                algorithm: "RSA-SHA256",
                signingKeyId: "config-signing-2026-01");

            Assert.Equal("abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789", package.ConfigurationHash);
            Assert.Equal("Base64SignatureValue==", package.Signature);
            Assert.Equal("RSA-SHA256", package.SignatureAlgorithm);
            Assert.Equal("config-signing-2026-01", package.SigningKeyId);

            // Re-signing must throw InvalidOperationException
            Assert.Throws<InvalidOperationException>(() => package.SetCryptographicSignature(
                hash: "0000000000000000000000000000000000000000000000000000000000000000",
                signature: "NewSig==",
                algorithm: "RSA-SHA256",
                signingKeyId: "config-signing-2026-02"));
        }

        [Fact]
        public async Task CommandHandlers_CreateFullAndDelta_PersistsCryptographicMetadataInDatabase()
        {
            using var dbContext = CreateInMemoryDbContext();
            var pkgRepo = new ConfigurationPackageRepository(dbContext);
            var keyRepo = new ConfigurationKeyRegistryRepository(dbContext);
            var keyProvider = new SigningPrivateKeyProvider(Microsoft.Extensions.Options.Options.Create(new Sayra.Backend.Infrastructure.Configuration.Options.SecurityOptions()));
            var keyRegistry = new ConfigurationKeyRegistry(keyRepo, keyProvider, dbContext);

            var validator = new ConfigurationValidatorService();
            var normalizer = new ConfigurationNormalizer(validator);
            var deltaEngine = new ConfigurationDeltaEngine(normalizer, validator);
            var serializer = new CanonicalConfigurationSerializer();
            var hashService = new ConfigurationHashService(serializer);
            var cryptoService = new CryptographicService();
            var signingService = new ConfigurationSigningService(serializer, hashService, keyRegistry, keyProvider, cryptoService);

            var createFullHandler = new CreateFullConfigurationVersionCommandHandler(pkgRepo, normalizer, validator, dbContext, signingService);
            var createDeltaHandler = new CreateDeltaConfigurationVersionCommandHandler(pkgRepo, deltaEngine, validator, dbContext, signingService);

            string fullPayload = @"{
              ""version"": ""1.0"",
              ""server"": { ""ipAddress"": ""127.0.0.1"", ""port"": 5000 },
              ""discovery"": { ""enabled"": true, ""port"": 37020 },
              ""heartbeat"": { ""intervalSeconds"": 10, ""timeoutSeconds"": 30 },
              ""kiosk"": { ""enabled"": true, ""allowShellEscape"": false, ""autoLoginGamer"": false, ""idleTimeoutMinutes"": 15 },
              ""localization"": { ""culture"": ""en-US"", ""timeZone"": ""UTC"" },
              ""security"": { ""enableSsl"": true, ""requireEncryption"": true, ""maxFailedAttempts"": 5 }
            }";

            // 1. Create v1 Full
            var cmdFull = new CreateFullConfigurationVersionCommand("default", fullPayload);
            var fullRes = await createFullHandler.HandleAsync(cmdFull);

            Assert.True(fullRes.IsSuccess, fullRes.ErrorMessage);
            Assert.NotNull(fullRes.Value);
            Assert.NotEmpty(fullRes.Value.ConfigurationHash!);
            Assert.NotEmpty(fullRes.Value.Signature!);
            Assert.Equal("RSA-SHA256", fullRes.Value.SignatureAlgorithm);
            Assert.NotEmpty(fullRes.Value.SigningKeyId!);

            // Verify v1 signature using IConfigurationSigningService
            var verifyV1 = await signingService.VerifyPackageAsync(fullRes.Value.Content, fullRes.Value.ConfigurationHash!, fullRes.Value.Signature!, fullRes.Value.SigningKeyId!);
            Assert.True(verifyV1.IsValid, verifyV1.FailureReason);

            // 2. Create v2 Delta
            string deltaPayload = @"[{""op"":""replace"",""path"":""/heartbeat/intervalSeconds"",""value"":15}]";
            var cmdDelta = new CreateDeltaConfigurationVersionCommand("default", 1, deltaPayload);
            var deltaRes = await createDeltaHandler.HandleAsync(cmdDelta);

            Assert.True(deltaRes.IsSuccess, deltaRes.ErrorMessage);
            Assert.NotNull(deltaRes.Value);
            Assert.NotEmpty(deltaRes.Value.ConfigurationHash!);
            Assert.NotEmpty(deltaRes.Value.Signature!);

            // Verify v2 delta signature using IConfigurationSigningService
            var verifyV2 = await signingService.VerifyPackageAsync(deltaRes.Value.Content, deltaRes.Value.ConfigurationHash!, deltaRes.Value.Signature!, deltaRes.Value.SigningKeyId!);
            Assert.True(verifyV2.IsValid, verifyV2.FailureReason);
        }
    }
}
