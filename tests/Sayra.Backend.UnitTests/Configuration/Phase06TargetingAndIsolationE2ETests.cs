using System;
using System.Collections.Generic;
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
    public class Phase06TargetingAndIsolationE2ETests
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
                .UseInMemoryDatabase(databaseName: $"Targeting_E2E_{Guid.NewGuid():N}")
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            return new ApplicationDbContext(options);
        }

        [Fact]
        public async Task MultiTier_Targeting_Hierarchy_Applies_Workstation_Over_Group_Over_Site_Over_Global()
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

            var (keyId, pubPem, privPem) = SigningPrivateKeyProvider.GetOrCreateEphemeralKeyPair("key-tgt-01");
            privateKeyProvider.RegisterTestKeyPair("key-tgt-01", pubPem, privPem);
            await keyRegistryRepo.AddAsync(ConfigurationSigningKey.Create("key-tgt-01", pubPem, "RSA-SHA256", SigningKeyStatus.Active));

            var org = new Organization { Name = "Org Target Hierarchy", Code = "ORGTGT", Status = "Active" };
            await orgRepo.AddAsync(org);

            var site = new Site { OrganizationId = org.Id, Name = "Site Alpha", Code = "SITEALPHA", Status = "Active" };
            await siteRepo.AddAsync(site);

            var group = new WorkstationGroup { OrganizationId = org.Id, SiteId = site.Id, Name = "VIP Group", Code = "GRPVIP", Status = "Active" };
            await groupRepo.AddAsync(group);

            var ws = new Workstation { PcId = "PC-TGT-01", Hostname = "pc-tgt-01", IpAddress = "10.0.0.1", MacAddress = "00:11:22:33:44:04", OrganizationEntityId = org.Id, SiteEntityId = site.Id };
            await wsRepo.AddAsync(ws);
            await dbContext.SaveChangesAsync();

            await groupRepo.AddMemberAsync(new WorkstationGroupMember { WorkstationGroupId = group.Id, WorkstationId = ws.Id, JoinedAt = DateTime.UtcNow });
            await dbContext.SaveChangesAsync();

            // Setup Global (port=5000), Site (port=5001), Group (port=5002), Workstation (intervalSeconds=5)
            var globalPkg = ConfigurationPackage.CreateFull("global", 1, GetConfigJson(5000));
            var sitePkg = ConfigurationPackage.CreateFull("site", 1, GetConfigJson(5001));
            var groupPkg = ConfigurationPackage.CreateFull("group", 1, GetConfigJson(5002));
            var wsPkg = ConfigurationPackage.CreateFull("workstation", 1, "{\"heartbeat\":{\"intervalSeconds\":5}}");

            foreach (var p in new[] { globalPkg, sitePkg, groupPkg, wsPkg })
            {
                var sign = await signingService.SignPackageAsync(p.Content);
                p.SetCryptographicSignature(sign.Hash, sign.Signature, sign.Algorithm, sign.KeyId);
                await pkgRepo.AddAsync(p);
            }

            var globalTgt = ConfigurationTarget.CreateGlobal(org.Id);
            var siteTgt = ConfigurationTarget.CreateSite(org.Id, site.Id);
            var groupTgt = ConfigurationTarget.CreateGroup(org.Id, group.Id, site.Id);
            var wsTgt = ConfigurationTarget.CreateWorkstation(org.Id, ws.Id, site.Id, group.Id);

            foreach (var t in new[] { globalTgt, siteTgt, groupTgt, wsTgt })
            {
                await targetRepo.AddAsync(t);
            }

            await assignRepo.AddAsync(ConfigurationAssignment.Create(globalPkg.Id, globalTgt.Id));
            await assignRepo.AddAsync(ConfigurationAssignment.Create(sitePkg.Id, siteTgt.Id));
            await assignRepo.AddAsync(ConfigurationAssignment.Create(groupPkg.Id, groupTgt.Id));
            await assignRepo.AddAsync(ConfigurationAssignment.Create(wsPkg.Id, wsTgt.Id));

            foreach (var (p, t) in new[] { (globalPkg, globalTgt), (sitePkg, siteTgt), (groupPkg, groupTgt), (wsPkg, wsTgt) })
            {
                var pub = ConfigurationPublication.Create(p.Id, 1, "v1", t.Id, org.Id, p.ConfigurationHash!, p.Signature!, p.SigningKeyId!, "RSA-SHA256", "admin");
                pub.Publish("admin");
                pub.Activate("admin");
                await pubRepo.AddAsync(pub);
            }
            await dbContext.SaveChangesAsync();

            // Resolve Effective Configuration
            var resolver = new ConfigurationResolver(wsRepo, orgRepo, siteRepo, groupRepo, assignRepo, targetRepo, pkgRepo, deltaEngine, normalizer, validator, pubRepo);
            var res = await resolver.ResolveEffectiveConfigurationAsync(ws.Id);
            Assert.True(res.IsSuccess);

            using var doc = System.Text.Json.JsonDocument.Parse(res.Value!.EffectiveConfigurationJson);
            // Port comes from Group layer (5002) overriding Site (5001) and Global (5000)
            Assert.Equal(5002, doc.RootElement.GetProperty("server").GetProperty("port").GetInt32());
            // Heartbeat interval comes from Workstation layer (5) overriding Group/Site/Global (10)
            Assert.Equal(5, doc.RootElement.GetProperty("heartbeat").GetProperty("intervalSeconds").GetInt32());
            // 4 applied sources in order: Global, Site, Group, Workstation
            Assert.Equal(4, res.Value!.AppliedSources.Count);
        }

        [Fact]
        public async Task MultiTenant_Isolation_Cross_Organization_Sync_Request_Is_Rejected()
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

            var (keyId, pubPem, privPem) = SigningPrivateKeyProvider.GetOrCreateEphemeralKeyPair("key-iso-01");
            privateKeyProvider.RegisterTestKeyPair("key-iso-01", pubPem, privPem);
            await keyRegistryRepo.AddAsync(ConfigurationSigningKey.Create("key-iso-01", pubPem, "RSA-SHA256", SigningKeyStatus.Active));

            // Org A & Workstation A
            var orgA = new Organization { Name = "Tenant A", Code = "ORGA", Status = "Active" };
            await orgRepo.AddAsync(orgA);
            var wsA = new Workstation { PcId = "PC-TENANT-A", Hostname = "pc-a", IpAddress = "10.0.0.1", MacAddress = "00:11:22:33:44:A1", OrganizationEntityId = orgA.Id };
            await wsRepo.AddAsync(wsA);

            // Org B
            var orgB = new Organization { Name = "Tenant B", Code = "ORGB", Status = "Active" };
            await orgRepo.AddAsync(orgB);
            await dbContext.SaveChangesAsync();

            var resolver = new ConfigurationResolver(wsRepo, orgRepo, siteRepo, groupRepo, assignRepo, targetRepo, pkgRepo, deltaEngine, normalizer, validator, pubRepo);
            var syncHandler = new SynchronizeConfigurationQueryHandler(wsRepo, resolver, signingService, hashService, canonicalSerializer, pkgRepo, deltaEngine, NullLogger<SynchronizeConfigurationQueryHandler>.Instance);

            // Workstation A belongs to Org A, but request specifies Org B
            var query = new SynchronizeConfigurationQuery(ClientPcId: wsA.PcId, ClientVersion: null, WorkstationId: wsA.Id, OrganizationId: orgB.Id);
            var res = await syncHandler.HandleAsync(query);

            Assert.False(res.IsSuccess);
            Assert.Equal("CROSS_ORGANIZATION_ACCESS_DENIED", res.ErrorCode);
        }
    }
}
