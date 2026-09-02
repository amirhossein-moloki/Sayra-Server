using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Sayra.Backend.Application.Configuration;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Infrastructure.Persistence;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class ConfigurationIntegrationTests
    {
        private static string GetSamplePayload(int port = 5000, int interval = 10, int timeout = 30)
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
                ""timeoutSeconds"": {timeout}
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

        private static ApplicationDbContext CreateInMemoryDbContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            return new ApplicationDbContext(options);
        }

        [Fact]
        public async Task ConfigurationPackageRepository_GetLatestVersion_ReturnsHighestVersionNumber()
        {
            using var dbContext = CreateInMemoryDbContext();
            var repo = new ConfigurationPackageRepository(dbContext);

            var pkg1 = ConfigurationPackage.CreateFull("default", 1, "{}");
            var pkg2 = ConfigurationPackage.CreateFull("default", 2, "{}");

            await repo.AddAsync(pkg1);
            await repo.AddAsync(pkg2);
            await dbContext.SaveChangesAsync();

            var latest = await repo.GetLatestVersionAsync("default");
            Assert.NotNull(latest);
            Assert.Equal(2, latest.VersionNumber);
        }

        [Fact]
        public async Task CreateFullAndDeltaVersionCommandHandlers_PersistAndReconstructSuccessfully()
        {
            using var dbContext = CreateInMemoryDbContext();
            var repo = new ConfigurationPackageRepository(dbContext);
            var validator = new ConfigurationValidatorService();
            var normalizer = new ConfigurationNormalizer(validator);
            var deltaEngine = new ConfigurationDeltaEngine(normalizer, validator);

            var createFullHandler = new CreateFullConfigurationVersionCommandHandler(repo, normalizer, validator, dbContext);
            var createDeltaHandler = new CreateDeltaConfigurationVersionCommandHandler(repo, deltaEngine, validator, dbContext);
            var reconstructHandler = new ReconstructConfigurationCommandHandler(repo, deltaEngine);

            // 1. Create v1 Full Package
            var cmdFull = new CreateFullConfigurationVersionCommand("default", GetSamplePayload(5000, 10, 30));
            var res1 = await createFullHandler.HandleAsync(cmdFull);

            Assert.True(res1.IsSuccess);
            Assert.NotNull(res1.Value);
            Assert.Equal(1, res1.Value.VersionNumber);
            Assert.Equal(ConfigurationPayloadType.Full, res1.Value.PayloadType);

            // 2. Create v2 Delta Package (replace intervalSeconds with 15)
            var deltaPayload = @"[{""op"":""replace"",""path"":""/heartbeat/intervalSeconds"",""value"":15}]";
            var cmdDelta = new CreateDeltaConfigurationVersionCommand("default", 1, deltaPayload);
            var res2 = await createDeltaHandler.HandleAsync(cmdDelta);

            Assert.True(res2.IsSuccess);
            Assert.NotNull(res2.Value);
            Assert.Equal(2, res2.Value.VersionNumber);
            Assert.Equal(1, res2.Value.BaseVersionNumber);
            Assert.Equal(ConfigurationPayloadType.Delta, res2.Value.PayloadType);

            // 3. Reconstruct v2
            var reconCmd = new ReconstructConfigurationCommand("default", 2);
            var reconRes = await reconstructHandler.HandleAsync(reconCmd);

            Assert.True(reconRes.IsSuccess);
            Assert.Contains(@"""intervalSeconds"":15", reconRes.Value);
        }

        [Fact]
        public async Task DeltaChainBuilding_ReturnsChainAndEnforcesMaxChainLength()
        {
            using var dbContext = CreateInMemoryDbContext();
            var repo = new ConfigurationPackageRepository(dbContext);

            // Seed v1 Full
            await repo.AddAsync(ConfigurationPackage.CreateFull("default", 1, GetSamplePayload()));
            // Seed v2 Delta
            await repo.AddAsync(ConfigurationPackage.CreateDelta("default", 2, 1, @"[{""op"":""replace"",""path"":""/heartbeat/intervalSeconds"",""value"":15}]"));
            // Seed v3 Delta
            await repo.AddAsync(ConfigurationPackage.CreateDelta("default", 3, 2, @"[{""op"":""replace"",""path"":""/heartbeat/timeoutSeconds"",""value"":45}]"));
            await dbContext.SaveChangesAsync();

            var handler = new BuildDeltaChainCommandHandler(repo);

            // Query v1 to v3 with max chain 10
            var res = await handler.HandleAsync(new BuildDeltaChainCommand("default", 1, 3, MaxChainLength: 10));

            Assert.True(res.IsSuccess);
            Assert.NotNull(res.Value);
            Assert.True(res.Value.CanUseDeltaChain);
            Assert.Equal(2, res.Value.Chain.Count);

            // Query with max chain 1 (exceeded)
            var resExceeded = await handler.HandleAsync(new BuildDeltaChainCommand("default", 1, 3, MaxChainLength: 1));

            Assert.True(resExceeded.IsSuccess);
            Assert.NotNull(resExceeded.Value);
            Assert.False(resExceeded.Value.CanUseDeltaChain);
            Assert.Contains("exceeds maximum allowed chain length", resExceeded.Value.FallbackReason);
        }

        [Fact]
        public async Task ConfigurationResolver_IntegrationWithInMemoryDbContext_ResolvesHierarchyAndFieldMerge()
        {
            using var dbContext = CreateInMemoryDbContext();

            var pkgRepo = new ConfigurationPackageRepository(dbContext);
            var targetRepo = new ConfigurationTargetRepository(dbContext);
            var assignRepo = new ConfigurationAssignmentRepository(dbContext);
            var groupRepo = new WorkstationGroupRepository(dbContext);
            var wsRepo = new Repository<Workstation>(dbContext);
            var orgRepo = new Repository<Organization>(dbContext);
            var siteRepo = new Repository<Site>(dbContext);

            var validator = new ConfigurationValidatorService();
            var normalizer = new ConfigurationNormalizer(validator);
            var deltaEngine = new ConfigurationDeltaEngine(normalizer, validator);

            // 1. Seed Organization, Site, Group, Workstation
            var org = new Organization { Name = "Org Integration", Code = "ORGINTEG", Status = "Active" };
            await orgRepo.AddAsync(org);

            var site = new Site { OrganizationId = org.Id, Name = "Site Integ", Code = "SITEINTEG", Status = "Active" };
            await siteRepo.AddAsync(site);

            var group = new WorkstationGroup { OrganizationId = org.Id, SiteId = site.Id, Name = "Group Integ", Code = "GRPINTEG", Status = "Active" };
            await groupRepo.AddAsync(group);

            var ws = new Workstation { PcId = "PC-INTEG-01", Hostname = "pc-integ-01", IpAddress = "10.0.0.50", MacAddress = "00:11:22:33:44:55", OrganizationEntityId = org.Id, SiteEntityId = site.Id };
            await wsRepo.AddAsync(ws);

            await dbContext.SaveChangesAsync();

            var member = new WorkstationGroupMember { WorkstationGroupId = group.Id, WorkstationId = ws.Id, JoinedAt = DateTime.UtcNow };
            await groupRepo.AddMemberAsync(member);
            await dbContext.SaveChangesAsync();

            // 2. Create Global and Workstation Targets & Packages
            var globalTarget = ConfigurationTarget.CreateGlobal(org.Id);
            await targetRepo.AddAsync(globalTarget);

            var wsTarget = ConfigurationTarget.CreateWorkstation(org.Id, ws.Id, site.Id, group.Id);
            await targetRepo.AddAsync(wsTarget);

            var globalPkg = ConfigurationPackage.CreateFull("global-pkg", 1, GetSamplePayload(5000, 10, 30));
            await pkgRepo.AddAsync(globalPkg);

            var wsPkg = ConfigurationPackage.CreateFull("ws-pkg", 2, "{\"heartbeat\":{\"intervalSeconds\":5}}");
            await pkgRepo.AddAsync(wsPkg);

            await dbContext.SaveChangesAsync();

            // 3. Assign Packages to Targets
            var globalAssign = ConfigurationAssignment.Create(globalPkg.Id, globalTarget.Id);
            var wsAssign = ConfigurationAssignment.Create(wsPkg.Id, wsTarget.Id);

            await assignRepo.AddAsync(globalAssign);
            await assignRepo.AddAsync(wsAssign);
            await dbContext.SaveChangesAsync();

            // 4. Resolve Effective Configuration
            var resolver = new ConfigurationResolver(
                wsRepo, orgRepo, siteRepo, groupRepo, assignRepo, targetRepo, pkgRepo,
                deltaEngine, normalizer, validator);

            var res = await resolver.ResolveEffectiveConfigurationAsync(ws.Id);

            Assert.True(res.IsSuccess, res.ErrorMessage);
            Assert.NotNull(res.Value);

            using var doc = System.Text.Json.JsonDocument.Parse(res.Value.EffectiveConfigurationJson);
            Assert.Equal(5000, doc.RootElement.GetProperty("server").GetProperty("port").GetInt32()); // Global inherited
            Assert.Equal(5, doc.RootElement.GetProperty("heartbeat").GetProperty("intervalSeconds").GetInt32()); // Workstation override
            Assert.Equal(2, res.Value.AppliedSources.Count);
        }
    }
}
