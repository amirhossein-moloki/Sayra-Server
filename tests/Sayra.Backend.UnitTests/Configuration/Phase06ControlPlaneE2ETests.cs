using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Configuration;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Infrastructure.Configuration.Options;
using Sayra.Backend.Infrastructure.Persistence;
using Sayra.Backend.Infrastructure.Security;
using Xunit;

namespace Sayra.Backend.UnitTests.Configuration
{
    public class Phase06ControlPlaneE2ETests
    {
        private static string GetValidSampleJson(int port = 5000, int interval = 10)
        {
            return $@"{{
              ""version"": ""1.0"",
              ""server"": {{
                ""ipAddress"": ""192.168.1.100"",
                ""port"": {port}
              }},
              ""discovery"": {{
                ""enabled"": true,
                ""port"": 37020
              }},
              ""heartbeat"": {{
                ""intervalSeconds"": {interval},
                ""timeoutSeconds"": 30
              }},
              ""kiosk"": {{
                ""enabled"": true,
                ""allowShellEscape"": false,
                ""autoLoginGamer"": false,
                ""idleTimeoutMinutes"": 15
              }},
              ""localization"": {{
                ""culture"": ""en-US"",
                ""timeZone"": ""UTC""
              }},
              ""security"": {{
                ""enableSsl"": true,
                ""requireEncryption"": true,
                ""maxFailedAttempts"": 5
              }}
            }}";
        }

        private static ApplicationDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: $"E2E_Config_{Guid.NewGuid():N}")
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            return new ApplicationDbContext(options);
        }

        [Fact]
        public async Task Full_ControlPlane_Lifecycle_Flow_Succeeds_EndToEnd()
        {
            using var dbContext = CreateDbContext();

            var pkgRepo = new ConfigurationPackageRepository(dbContext);
            var targetRepo = new ConfigurationTargetRepository(dbContext);
            var assignRepo = new ConfigurationAssignmentRepository(dbContext);
            var pubRepo = new ConfigurationPublicationRepository(dbContext);
            var keyRegistryRepo = new ConfigurationKeyRegistryRepository(dbContext);
            var wsRepo = new Repository<Workstation>(dbContext);
            var orgRepo = new Repository<Organization>(dbContext);
            var siteRepo = new Repository<Site>(dbContext);
            var groupRepo = new WorkstationGroupRepository(dbContext);

            var validator = new ConfigurationValidatorService();
            var normalizer = new ConfigurationNormalizer(validator);
            var deltaEngine = new ConfigurationDeltaEngine(normalizer, validator);
            var canonicalSerializer = new CanonicalConfigurationSerializer();
            var hashService = new ConfigurationHashService();
            var cryptoService = new CryptographicService();
            var privateKeyProvider = new SigningPrivateKeyProvider(Options.Create(new SecurityOptions()));
            var keyRegistry = new ConfigurationKeyRegistry(keyRegistryRepo, privateKeyProvider, dbContext);
            var signingService = new ConfigurationSigningService(canonicalSerializer, hashService, keyRegistry, privateKeyProvider, cryptoService);

            // Seed signing key
            var (ephemeralKeyId, ephemeralPublicPem, ephemeralPrivatePem) = SigningPrivateKeyProvider.GetOrCreateEphemeralKeyPair("key-e2e-01");
            privateKeyProvider.RegisterTestKeyPair("key-e2e-01", ephemeralPublicPem, ephemeralPrivatePem);
            await keyRegistryRepo.AddAsync(ConfigurationSigningKey.Create("key-e2e-01", ephemeralPublicPem, "RSA-SHA256", SigningKeyStatus.Active));
            await dbContext.SaveChangesAsync();

            // 1. Seed Organization & Workstation
            var org = new Organization { Name = "E2E Gaming Inc", Code = "E2EORG", Status = "Active" };
            await orgRepo.AddAsync(org);

            var site = new Site { OrganizationId = org.Id, Name = "Main Arena", Code = "MAINARENA", Status = "Active" };
            await siteRepo.AddAsync(site);

            var ws = new Workstation
            {
                PcId = "PC-E2E-001",
                Hostname = "pc-e2e-001",
                IpAddress = "192.168.1.50",
                MacAddress = "00:11:22:33:44:99",
                OrganizationEntityId = org.Id,
                SiteEntityId = site.Id
            };
            await wsRepo.AddAsync(ws);
            await dbContext.SaveChangesAsync();

            // 2. Validate & Normalize Raw Configuration JSON
            string rawContent = GetValidSampleJson(port: 5000, interval: 10);
            var valResult = validator.Validate(rawContent);
            Assert.True(valResult.IsValid);

            string normalizedContent = normalizer.NormalizeToJson(rawContent);
            Assert.False(string.IsNullOrWhiteSpace(normalizedContent));

            // 3. Create Immutable Version & Sign Package
            var createFullHandler = new CreateFullConfigurationVersionCommandHandler(
                pkgRepo, normalizer, validator, dbContext, signingService);

            var pkgResult = await createFullHandler.HandleAsync(new CreateFullConfigurationVersionCommand("e2e-global-spec", rawContent, "1.0", "admin"));
            Assert.True(pkgResult.IsSuccess);
            var package = pkgResult.Value!;
            Assert.Equal(1, package.VersionNumber);
            Assert.NotNull(package.Signature);
            Assert.NotNull(package.ConfigurationHash);

            // 4. Target & Assign
            var target = ConfigurationTarget.CreateGlobal(org.Id);
            await targetRepo.AddAsync(target);

            var assignment = ConfigurationAssignment.Create(package.Id, target.Id, "admin");
            await assignRepo.AddAsync(assignment);
            await dbContext.SaveChangesAsync();

            // 5. Prepare, Publish & Activate
            var prepareHandler = new PreparePublicationCommandHandler(pkgRepo, targetRepo, assignRepo, pubRepo, signingService, validator, dbContext);
            var prepareRes = await prepareHandler.HandleAsync(new PreparePublicationCommand(package.Id, target.Id, "admin"));
            Assert.True(prepareRes.IsSuccess);

            var publishHandler = new PublishConfigurationCommandHandler(pkgRepo, targetRepo, assignRepo, pubRepo, signingService, validator, dbContext);
            var pubRes = await publishHandler.HandleAsync(new PublishConfigurationCommand(package.Id, target.Id, "admin"));
            Assert.True(pubRes.IsSuccess);

            var activateHandler = new ActivateConfigurationCommandHandler(pubRepo, pkgRepo, signingService, dbContext, targetRepo);
            var actRes = await activateHandler.HandleAsync(new ActivateConfigurationCommand(pubRes.Value!.Id, "admin"));
            Assert.True(actRes.IsSuccess);
            Assert.Equal("Active", actRes.Value!.Status);

            // 6. Resolve Effective Configuration for Workstation
            var resolver = new ConfigurationResolver(wsRepo, orgRepo, siteRepo, groupRepo, assignRepo, targetRepo, pkgRepo, deltaEngine, normalizer, validator, pubRepo);
            var resolveRes = await resolver.ResolveEffectiveConfigurationAsync(ws.Id);
            Assert.True(resolveRes.IsSuccess);
            Assert.Contains(@"""port"":5000", resolveRes.Value!.EffectiveConfigurationJson);

            // 7. Client Synchronize API Call
            var syncHandler = new SynchronizeConfigurationQueryHandler(
                wsRepo, resolver, signingService, hashService, canonicalSerializer, pkgRepo, deltaEngine,
                NullLogger<SynchronizeConfigurationQueryHandler>.Instance);

            var syncQuery = new SynchronizeConfigurationQuery(ClientPcId: ws.PcId, ClientVersion: null, WorkstationId: ws.Id, OrganizationId: org.Id);
            var syncRes = await syncHandler.HandleAsync(syncQuery);
            Assert.True(syncRes.IsSuccess);

            var syncDto = syncRes.Value!;
            Assert.Equal(ConfigurationSyncStatus.FullPackage, syncDto.Status);
            Assert.Equal("v1", syncDto.Version);
            Assert.False(string.IsNullOrWhiteSpace(syncDto.Hash));
            Assert.False(string.IsNullOrWhiteSpace(syncDto.Signature));

            // 8. Client Signature Verification
            var verifyResult = await signingService.VerifyPackageAsync(
                resolveRes.Value!.EffectiveConfigurationJson,
                syncDto.Hash,
                syncDto.Signature,
                syncDto.KeyId!);

            Assert.True(verifyResult.IsValid, verifyResult.FailureReason);
        }
    }
}
