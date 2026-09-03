using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sayra.Backend.Application.Configuration;
using Sayra.Backend.Domain;
using Sayra.Backend.Infrastructure.Configuration.Options;
using Sayra.Backend.Infrastructure.Persistence;
using Sayra.Backend.Infrastructure.Security;
using Xunit;

namespace Sayra.Backend.UnitTests.Configuration
{
    public class Phase06LifecycleAndRollbackE2ETests
    {
        private static string GetConfigJson(int port) => $@"{{
          ""version"": ""1.0"",
          ""server"": {{ ""ipAddress"": ""192.168.1.1"", ""port"": {port} }},
          ""discovery"": {{ ""enabled"": true, ""port"": 37020 }},
          ""heartbeat"": {{ ""intervalSeconds"": 10, ""timeoutSeconds"": 30 }},
          ""kiosk"": {{ ""enabled"": true, ""allowShellEscape"": false, ""autoLoginGamer"": false, ""idleTimeoutMinutes"": 15 }},
          ""localization"": {{ ""culture"": ""en-US"", ""timeZone"": ""UTC"" }},
          ""security"": {{ ""enableSsl"": true, ""requireEncryption"": true, ""maxFailedAttempts"": 5 }}
        }}";

        private static ApplicationDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: $"Lifecycle_E2E_{Guid.NewGuid():N}")
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            return new ApplicationDbContext(options);
        }

        [Fact]
        public async Task VersionUpdate_Scenario_v44_To_v45_Supersedes_And_Serves_304_When_Current()
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

            // Register key
            var (keyId, pubPem, privPem) = SigningPrivateKeyProvider.GetOrCreateEphemeralKeyPair("key-upd-01");
            privateKeyProvider.RegisterTestKeyPair("key-upd-01", pubPem, privPem);
            await keyRegistryRepo.AddAsync(ConfigurationSigningKey.Create("key-upd-01", pubPem, "RSA-SHA256", SigningKeyStatus.Active));

            // Seed Org & Workstation
            var org = new Organization { Name = "Org Update", Code = "ORGUPD", Status = "Active" };
            await orgRepo.AddAsync(org);
            var ws = new Workstation { PcId = "PC-UPD-01", Hostname = "pc-upd-01", IpAddress = "10.0.0.1", MacAddress = "00:11:22:33:44:01", OrganizationEntityId = org.Id };
            await wsRepo.AddAsync(ws);
            await dbContext.SaveChangesAsync();

            var target = ConfigurationTarget.CreateGlobal(org.Id);
            await targetRepo.AddAsync(target);

            // Create v44
            var v44Pkg = ConfigurationPackage.CreateFull("default-spec", 44, GetConfigJson(5044));
            var sign44 = await signingService.SignPackageAsync(v44Pkg.Content);
            v44Pkg.SetCryptographicSignature(sign44.Hash, sign44.Signature, sign44.Algorithm, sign44.KeyId);
            await pkgRepo.AddAsync(v44Pkg);

            var assign44 = ConfigurationAssignment.Create(v44Pkg.Id, target.Id);
            await assignRepo.AddAsync(assign44);

            var pub44 = ConfigurationPublication.Create(v44Pkg.Id, 44, "v44", target.Id, org.Id, sign44.Hash, sign44.Signature, sign44.KeyId, "RSA-SHA256", "admin");
            pub44.Publish("admin");
            pub44.Activate("admin");
            await pubRepo.AddAsync(pub44);
            await dbContext.SaveChangesAsync();

            // Resolver & Sync API for v44
            var resolver = new ConfigurationResolver(wsRepo, orgRepo, siteRepo, groupRepo, assignRepo, targetRepo, pkgRepo, deltaEngine, normalizer, validator, pubRepo);
            var syncHandler = new SynchronizeConfigurationQueryHandler(wsRepo, resolver, signingService, hashService, canonicalSerializer, pkgRepo, deltaEngine, NullLogger<SynchronizeConfigurationQueryHandler>.Instance);

            var syncRes44 = await syncHandler.HandleAsync(new SynchronizeConfigurationQuery(ws.PcId, ClientVersion: 44, WorkstationId: ws.Id, OrganizationId: org.Id));
            Assert.True(syncRes44.IsSuccess);
            Assert.Equal(ConfigurationSyncStatus.UpToDate, syncRes44.Value!.Status);

            // Now publish v45
            var v45Pkg = ConfigurationPackage.CreateFull("default-spec", 45, GetConfigJson(5045));
            var sign45 = await signingService.SignPackageAsync(v45Pkg.Content);
            v45Pkg.SetCryptographicSignature(sign45.Hash, sign45.Signature, sign45.Algorithm, sign45.KeyId);
            await pkgRepo.AddAsync(v45Pkg);

            var assign45 = ConfigurationAssignment.Create(v45Pkg.Id, target.Id);
            await assignRepo.AddAsync(assign45);

            var pub45 = ConfigurationPublication.Create(v45Pkg.Id, 45, "v45", target.Id, org.Id, sign45.Hash, sign45.Signature, sign45.KeyId, "RSA-SHA256", "admin");
            pub45.Publish("admin");
            await pubRepo.AddAsync(pub45);
            await dbContext.SaveChangesAsync();

            // Activate v45 (supersedes v44)
            var activateHandler = new ActivateConfigurationCommandHandler(pubRepo, pkgRepo, signingService, dbContext, targetRepo);
            var actRes = await activateHandler.HandleAsync(new ActivateConfigurationCommand(pub45.Id, "admin"));
            Assert.True(actRes.IsSuccess, actRes.ErrorMessage);

            // Verify v44 is Superseded
            var updatedPub44 = await pubRepo.GetByIdAsync(pub44.Id);
            Assert.Equal(ConfigurationLifecycleState.Superseded, updatedPub44!.Status);

            // Sync query from workstation on v44 -> should receive v45 update!
            var syncRes45 = await syncHandler.HandleAsync(new SynchronizeConfigurationQuery(ws.PcId, ClientVersion: 44, WorkstationId: ws.Id, OrganizationId: org.Id));
            Assert.True(syncRes45.IsSuccess);
            Assert.Equal(45, syncRes45.Value!.VersionNumber);

            // Sync query from workstation now on v45 -> 304 Not Modified!
            var syncResCurrent = await syncHandler.HandleAsync(new SynchronizeConfigurationQuery(ws.PcId, ClientVersion: 45, WorkstationId: ws.Id, OrganizationId: org.Id));
            Assert.True(syncResCurrent.IsSuccess);
            Assert.Equal(ConfigurationSyncStatus.UpToDate, syncResCurrent.Value!.Status);
        }

        [Fact]
        public async Task Rollback_Scenario_v44_To_v45_Rollback_Creates_New_Immutable_Version_v46()
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

            var (keyId, pubPem, privPem) = SigningPrivateKeyProvider.GetOrCreateEphemeralKeyPair("key-rb-01");
            privateKeyProvider.RegisterTestKeyPair("key-rb-01", pubPem, privPem);
            await keyRegistryRepo.AddAsync(ConfigurationSigningKey.Create("key-rb-01", pubPem, "RSA-SHA256", SigningKeyStatus.Active));

            var org = new Organization { Name = "Org Rollback", Code = "ORGRB", Status = "Active" };
            await orgRepo.AddAsync(org);
            var ws = new Workstation { PcId = "PC-RB-01", Hostname = "pc-rb-01", IpAddress = "10.0.0.1", MacAddress = "00:11:22:33:44:02", OrganizationEntityId = org.Id };
            await wsRepo.AddAsync(ws);
            await dbContext.SaveChangesAsync();

            var target = ConfigurationTarget.CreateGlobal(org.Id);
            await targetRepo.AddAsync(target);

            // 1. Seed v44 Known-Good
            var v44Pkg = ConfigurationPackage.CreateFull("default", 44, GetConfigJson(5044));
            var sign44 = await signingService.SignPackageAsync(v44Pkg.Content);
            v44Pkg.SetCryptographicSignature(sign44.Hash, sign44.Signature, sign44.Algorithm, sign44.KeyId);
            await pkgRepo.AddAsync(v44Pkg);

            var pub44 = ConfigurationPublication.Create(v44Pkg.Id, 44, "v44", target.Id, org.Id, sign44.Hash, sign44.Signature, sign44.KeyId, "RSA-SHA256", "admin");
            pub44.Publish("admin");
            pub44.Activate("admin");
            await pubRepo.AddAsync(pub44);
            await assignRepo.AddAsync(ConfigurationAssignment.Create(v44Pkg.Id, target.Id));

            // 2. Seed v45 Failed Version
            var v45Pkg = ConfigurationPackage.CreateFull("default", 45, GetConfigJson(9999));
            var sign45 = await signingService.SignPackageAsync(v45Pkg.Content);
            v45Pkg.SetCryptographicSignature(sign45.Hash, sign45.Signature, sign45.Algorithm, sign45.KeyId);
            await pkgRepo.AddAsync(v45Pkg);

            var pub45 = ConfigurationPublication.Create(v45Pkg.Id, 45, "v45", target.Id, org.Id, sign45.Hash, sign45.Signature, sign45.KeyId, "RSA-SHA256", "admin");
            pub45.Publish("admin");
            pub44.Supersede(pub45.Id);
            pub45.Activate("admin");
            await pubRepo.AddAsync(pub45);
            await assignRepo.AddAsync(ConfigurationAssignment.Create(v45Pkg.Id, target.Id));
            await dbContext.SaveChangesAsync();

            // 3. Perform Rollback to Known-Good v44
            var rollbackHandler = new RollbackConfigurationCommandHandler(
                targetRepo, pkgRepo, assignRepo, pubRepo, signingService, validator, deltaEngine, dbContext);

            var rollbackCmd = new RollbackConfigurationCommand(
                ConfigurationTargetId: target.Id,
                KnownGoodVersionNumber: 44,
                FailedVersionNumber: 45,
                PackageName: "default",
                Reason: "v45 caused bad server port",
                Actor: "admin");

            var rbResult = await rollbackHandler.HandleAsync(rollbackCmd);
            Assert.True(rbResult.IsSuccess);

            var rbDto = rbResult.Value!;
            Assert.True(rbDto.IsRollback);
            Assert.Equal(46, rbDto.VersionNumber); // New immutable version 46!
            Assert.Equal(44, rbDto.SourceVersionNumber);
            Assert.Equal(45, rbDto.FailedVersionNumber);
            Assert.Equal("Active", rbDto.Status);

            // Historical versions v44 and v45 remain untouched in DB
            var pkg44 = await pkgRepo.GetByVersionNumberAsync("default", 44);
            Assert.NotNull(pkg44);
            var pkg45 = await pkgRepo.GetByVersionNumberAsync("default", 45);
            Assert.NotNull(pkg45);
            var pkg46 = await pkgRepo.GetByVersionNumberAsync("default", 46);
            Assert.NotNull(pkg46);

            // Resolver serves new rollback v46 with port 5044
            var resolver = new ConfigurationResolver(wsRepo, orgRepo, siteRepo, groupRepo, assignRepo, targetRepo, pkgRepo, deltaEngine, normalizer, validator, pubRepo);
            var res = await resolver.ResolveEffectiveConfigurationAsync(ws.Id);
            Assert.True(res.IsSuccess);
            Assert.Contains(@"""port"":5044", res.Value!.EffectiveConfigurationJson);
        }

        [Fact]
        public async Task Revocation_Scenario_Revoked_Publication_Is_Excluded_From_Resolver_And_Sync()
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

            var (keyId, pubPem, privPem) = SigningPrivateKeyProvider.GetOrCreateEphemeralKeyPair("key-rev-01");
            privateKeyProvider.RegisterTestKeyPair("key-rev-01", pubPem, privPem);
            await keyRegistryRepo.AddAsync(ConfigurationSigningKey.Create("key-rev-01", pubPem, "RSA-SHA256", SigningKeyStatus.Active));

            var org = new Organization { Name = "Org Revoke", Code = "ORGREV", Status = "Active" };
            await orgRepo.AddAsync(org);
            var ws = new Workstation { PcId = "PC-REV-01", Hostname = "pc-rev-01", IpAddress = "10.0.0.1", MacAddress = "00:11:22:33:44:03", OrganizationEntityId = org.Id };
            await wsRepo.AddAsync(ws);
            await dbContext.SaveChangesAsync();

            var target = ConfigurationTarget.CreateGlobal(org.Id);
            await targetRepo.AddAsync(target);

            var pkg = ConfigurationPackage.CreateFull("default", 1, GetConfigJson(5000));
            var sign = await signingService.SignPackageAsync(pkg.Content);
            pkg.SetCryptographicSignature(sign.Hash, sign.Signature, sign.Algorithm, sign.KeyId);
            await pkgRepo.AddAsync(pkg);

            var pub = ConfigurationPublication.Create(pkg.Id, 1, "v1", target.Id, org.Id, sign.Hash, sign.Signature, sign.KeyId, "RSA-SHA256", "admin");
            pub.Publish("admin");
            pub.Activate("admin");
            await pubRepo.AddAsync(pub);
            await assignRepo.AddAsync(ConfigurationAssignment.Create(pkg.Id, target.Id));
            await dbContext.SaveChangesAsync();

            // Revoke
            var revokeHandler = new RevokeConfigurationCommandHandler(pubRepo, pkgRepo, dbContext, targetRepo);
            var revRes = await revokeHandler.HandleAsync(new RevokeConfigurationCommand(pub.Id, "Security flaw detected", "admin"));
            Assert.True(revRes.IsSuccess);
            Assert.Equal("Revoked", revRes.Value!.Status);

            // Package is marked inactive
            var updatedPkg = await pkgRepo.GetByIdAsync(pkg.Id);
            Assert.False(updatedPkg!.IsActive);

            // Resolver excludes revoked publication
            var resolver = new ConfigurationResolver(wsRepo, orgRepo, siteRepo, groupRepo, assignRepo, targetRepo, pkgRepo, deltaEngine, normalizer, validator, pubRepo);
            var res = await resolver.ResolveEffectiveConfigurationAsync(ws.Id);
            Assert.True(res.IsSuccess);
            Assert.Empty(res.Value!.AppliedSources); // No active sources remain
        }
    }
}
