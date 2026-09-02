using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Configuration;
using Sayra.Backend.Application.Configuration.Models;
using Sayra.Backend.Domain;
using Sayra.Backend.Shared;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class ConfigurationResolverTests
    {
        private readonly Mock<IRepository<Workstation>> _workstationRepoMock;
        private readonly Mock<IRepository<Organization>> _orgRepoMock;
        private readonly Mock<IRepository<Site>> _siteRepoMock;
        private readonly Mock<IWorkstationGroupRepository> _groupRepoMock;
        private readonly Mock<IConfigurationAssignmentRepository> _assignmentRepoMock;
        private readonly Mock<IConfigurationTargetRepository> _targetRepoMock;
        private readonly Mock<IConfigurationPackageRepository> _packageRepoMock;
        private readonly IConfigurationDeltaEngine _deltaEngine;
        private readonly IConfigurationNormalizer _normalizer;
        private readonly IConfigurationValidator _validator;

        public ConfigurationResolverTests()
        {
            _workstationRepoMock = new Mock<IRepository<Workstation>>();
            _orgRepoMock = new Mock<IRepository<Organization>>();
            _siteRepoMock = new Mock<IRepository<Site>>();
            _groupRepoMock = new Mock<IWorkstationGroupRepository>();
            _assignmentRepoMock = new Mock<IConfigurationAssignmentRepository>();
            _targetRepoMock = new Mock<IConfigurationTargetRepository>();
            _packageRepoMock = new Mock<IConfigurationPackageRepository>();

            _normalizer = new ConfigurationNormalizer();
            _validator = new ConfigurationValidatorService();
            _deltaEngine = new ConfigurationDeltaEngine(_normalizer, _validator);
        }

        private ConfigurationResolver CreateResolver()
        {
            return new ConfigurationResolver(
                _workstationRepoMock.Object,
                _orgRepoMock.Object,
                _siteRepoMock.Object,
                _groupRepoMock.Object,
                _assignmentRepoMock.Object,
                _targetRepoMock.Object,
                _packageRepoMock.Object,
                _deltaEngine,
                _normalizer,
                _validator);
        }

        [Fact]
        public async Task GlobalOnly_ResolvesGlobalConfiguration()
        {
            var org = new Organization { Name = "Org", Code = "ORG", Status = "Active" };
            var ws = new Workstation { PcId = "PC-01", Hostname = "pc-01", IpAddress = "10.0.0.1", MacAddress = "00:11:22:33:44:55", OrganizationEntityId = org.Id };

            var globalTarget = ConfigurationTarget.CreateGlobal(org.Id);
            var defaultSchema = new SayraConfigurationSchema();
            defaultSchema.Server.Port = 5000;
            defaultSchema.Server.IpAddress = "10.0.0.1";
            defaultSchema.Heartbeat.IntervalSeconds = 10;
            defaultSchema.Heartbeat.TimeoutSeconds = 30;

            var globalPayload = _normalizer.NormalizeToJson(defaultSchema);
            var globalPkg = ConfigurationPackage.CreateFull("global-config", 1, globalPayload);
            var globalAssignment = ConfigurationAssignment.Create(globalPkg.Id, globalTarget.Id);

            SetupBasicContext(ws, org, new List<ConfigurationAssignment> { globalAssignment });
            SetupTargetAndPackage(globalTarget, globalPkg);

            var resolver = CreateResolver();
            var result = await resolver.ResolveEffectiveConfigurationAsync(ws.Id);

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.NotNull(result.Value);

            using var doc = JsonDocument.Parse(result.Value.EffectiveConfigurationJson);
            Assert.Equal(5000, doc.RootElement.GetProperty("server").GetProperty("port").GetInt32());
            Assert.Equal("10.0.0.1", doc.RootElement.GetProperty("server").GetProperty("ipAddress").GetString());
            Assert.Equal(10, doc.RootElement.GetProperty("heartbeat").GetProperty("intervalSeconds").GetInt32());

            Assert.Single(result.Value.AppliedSources);
            Assert.Equal("Global", result.Value.AppliedSources[0].TargetType);
        }

        [Fact]
        public async Task GlobalPlusSite_SiteOverridesSpecificFields()
        {
            var org = new Organization { Name = "Org", Code = "ORG", Status = "Active" };
            var site = new Site { OrganizationId = org.Id, Name = "Site A", Code = "SITEA", Status = "Active" };
            var ws = new Workstation { PcId = "PC-01", Hostname = "pc-01", IpAddress = "10.0.0.1", MacAddress = "00:11:22:33:44:55", OrganizationEntityId = org.Id, SiteEntityId = site.Id };

            _siteRepoMock.Setup(r => r.GetByIdAsync(site.Id, false, It.IsAny<CancellationToken>())).ReturnsAsync(site);

            var globalTarget = ConfigurationTarget.CreateGlobal(org.Id);
            var siteTarget = ConfigurationTarget.CreateSite(org.Id, site.Id);

            var defaultSchema = new SayraConfigurationSchema();
            defaultSchema.Server.Port = 5000;
            defaultSchema.Server.IpAddress = "10.0.0.1";
            defaultSchema.Heartbeat.IntervalSeconds = 10;
            defaultSchema.Heartbeat.TimeoutSeconds = 30;

            var globalPayload = _normalizer.NormalizeToJson(defaultSchema);
            var sitePayload = "{\"heartbeat\":{\"intervalSeconds\":15}}";

            var globalPkg = ConfigurationPackage.CreateFull("global-config", 1, globalPayload);
            var sitePkg = ConfigurationPackage.CreateFull("site-config", 2, sitePayload);

            var globalAssignment = ConfigurationAssignment.Create(globalPkg.Id, globalTarget.Id);
            var siteAssignment = ConfigurationAssignment.Create(sitePkg.Id, siteTarget.Id);

            SetupBasicContext(ws, org, new List<ConfigurationAssignment> { globalAssignment, siteAssignment });
            SetupTargetAndPackage(globalTarget, globalPkg);
            SetupTargetAndPackage(siteTarget, sitePkg);

            var resolver = CreateResolver();
            var result = await resolver.ResolveEffectiveConfigurationAsync(ws.Id);

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.NotNull(result.Value);
            using var doc = JsonDocument.Parse(result.Value.EffectiveConfigurationJson);
            // Server section inherited from Global
            Assert.Equal(5000, doc.RootElement.GetProperty("server").GetProperty("port").GetInt32());
            Assert.Equal("10.0.0.1", doc.RootElement.GetProperty("server").GetProperty("ipAddress").GetString());
            // Heartbeat section overridden by Site
            Assert.Equal(15, doc.RootElement.GetProperty("heartbeat").GetProperty("intervalSeconds").GetInt32());

            Assert.Equal(2, result.Value.AppliedSources.Count);
        }

        [Fact]
        public async Task FullHierarchy_WorkstationOverridesGroupSiteGlobal()
        {
            var org = new Organization { Name = "Org", Code = "ORG", Status = "Active" };
            var site = new Site { OrganizationId = org.Id, Name = "Site A", Code = "SITEA", Status = "Active" };
            var group = new WorkstationGroup { OrganizationId = org.Id, SiteId = site.Id, Name = "Group A", Code = "GRPA", Status = "Active" };
            var ws = new Workstation { PcId = "PC-01", Hostname = "pc-01", IpAddress = "10.0.0.1", MacAddress = "00:11:22:33:44:55", OrganizationEntityId = org.Id, SiteEntityId = site.Id };

            _siteRepoMock.Setup(r => r.GetByIdAsync(site.Id, false, It.IsAny<CancellationToken>())).ReturnsAsync(site);
            _groupRepoMock.Setup(r => r.GetWorkstationGroupIdsForWorkstationAsync(ws.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Guid> { group.Id });
            _groupRepoMock.Setup(r => r.GetByIdAsync(group.Id, false, It.IsAny<CancellationToken>())).ReturnsAsync(group);

            var globalTarget = ConfigurationTarget.CreateGlobal(org.Id);
            var siteTarget = ConfigurationTarget.CreateSite(org.Id, site.Id);
            var groupTarget = ConfigurationTarget.CreateGroup(org.Id, group.Id, site.Id);
            var wsTarget = ConfigurationTarget.CreateWorkstation(org.Id, ws.Id, site.Id, group.Id);

            var defaultSchema = new SayraConfigurationSchema();
            defaultSchema.Server.Port = 5000;
            defaultSchema.Heartbeat.IntervalSeconds = 10;
            defaultSchema.Heartbeat.TimeoutSeconds = 30;
            defaultSchema.Kiosk.Enabled = false;

            var globalPayload = _normalizer.NormalizeToJson(defaultSchema);
            var sitePayload = "{\"heartbeat\":{\"intervalSeconds\":15}}";
            var groupPayload = "{\"kiosk\":{\"enabled\":true}}";
            var wsPayload = "{\"heartbeat\":{\"intervalSeconds\":5}}";

            var globalPkg = ConfigurationPackage.CreateFull("g-pkg", 1, globalPayload);
            var sitePkg = ConfigurationPackage.CreateFull("s-pkg", 2, sitePayload);
            var groupPkg = ConfigurationPackage.CreateFull("grp-pkg", 3, groupPayload);
            var wsPkg = ConfigurationPackage.CreateFull("ws-pkg", 4, wsPayload);

            var globalAssign = ConfigurationAssignment.Create(globalPkg.Id, globalTarget.Id);
            var siteAssign = ConfigurationAssignment.Create(sitePkg.Id, siteTarget.Id);
            var groupAssign = ConfigurationAssignment.Create(groupPkg.Id, groupTarget.Id);
            var wsAssign = ConfigurationAssignment.Create(wsPkg.Id, wsTarget.Id);

            SetupBasicContext(ws, org, new List<ConfigurationAssignment> { globalAssign, siteAssign, groupAssign, wsAssign });
            SetupTargetAndPackage(globalTarget, globalPkg);
            SetupTargetAndPackage(siteTarget, sitePkg);
            SetupTargetAndPackage(groupTarget, groupPkg);
            SetupTargetAndPackage(wsTarget, wsPkg);

            var resolver = CreateResolver();
            var result = await resolver.ResolveEffectiveConfigurationAsync(ws.Id);

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.NotNull(result.Value);
            using var doc = JsonDocument.Parse(result.Value.EffectiveConfigurationJson);

            Assert.Equal(5000, doc.RootElement.GetProperty("server").GetProperty("port").GetInt32()); // Global
            Assert.True(doc.RootElement.GetProperty("kiosk").GetProperty("enabled").GetBoolean()); // Group
            Assert.Equal(5, doc.RootElement.GetProperty("heartbeat").GetProperty("intervalSeconds").GetInt32()); // Workstation wins over Site & Global

            Assert.Equal(4, result.Value.AppliedSources.Count);
        }

        [Fact]
        public async Task MultiGroup_DeterministicOrdering_ByGroupCode()
        {
            var org = new Organization { Name = "Org", Code = "ORG", Status = "Active" };
            var ws = new Workstation { PcId = "PC-01", Hostname = "pc-01", IpAddress = "10.0.0.1", MacAddress = "00:11:22:33:44:55", OrganizationEntityId = org.Id };

            var groupA = new WorkstationGroup { OrganizationId = org.Id, Name = "Group A", Code = "ALPHA", Status = "Active" };
            var groupB = new WorkstationGroup { OrganizationId = org.Id, Name = "Group B", Code = "BETA", Status = "Active" };

            _groupRepoMock.Setup(r => r.GetWorkstationGroupIdsForWorkstationAsync(ws.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Guid> { groupB.Id, groupA.Id }); // Passed in reverse order to test sorting
            _groupRepoMock.Setup(r => r.GetByIdAsync(groupA.Id, false, It.IsAny<CancellationToken>())).ReturnsAsync(groupA);
            _groupRepoMock.Setup(r => r.GetByIdAsync(groupB.Id, false, It.IsAny<CancellationToken>())).ReturnsAsync(groupB);

            var globalTarget = ConfigurationTarget.CreateGlobal(org.Id);
            var defaultSchema = new SayraConfigurationSchema();
            defaultSchema.Heartbeat.IntervalSeconds = 10;
            defaultSchema.Heartbeat.TimeoutSeconds = 40;
            var globalPayload = _normalizer.NormalizeToJson(defaultSchema);
            var globalPkg = ConfigurationPackage.CreateFull("g-pkg", 1, globalPayload);
            var globalAssign = ConfigurationAssignment.Create(globalPkg.Id, globalTarget.Id);

            var targetA = ConfigurationTarget.CreateGroup(org.Id, groupA.Id);
            var targetB = ConfigurationTarget.CreateGroup(org.Id, groupB.Id);

            var payloadA = "{\"heartbeat\":{\"intervalSeconds\":15}}";
            var payloadB = "{\"heartbeat\":{\"intervalSeconds\":20}}";

            var pkgA = ConfigurationPackage.CreateFull("pkg-a", 1, payloadA);
            var pkgB = ConfigurationPackage.CreateFull("pkg-b", 1, payloadB);

            var assignA = ConfigurationAssignment.Create(pkgA.Id, targetA.Id);
            var assignB = ConfigurationAssignment.Create(pkgB.Id, targetB.Id);

            SetupBasicContext(ws, org, new List<ConfigurationAssignment> { globalAssign, assignB, assignA });
            SetupTargetAndPackage(globalTarget, globalPkg);
            SetupTargetAndPackage(targetA, pkgA);
            SetupTargetAndPackage(targetB, pkgB);

            var resolver = CreateResolver();
            var result = await resolver.ResolveEffectiveConfigurationAsync(ws.Id);

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.NotNull(result.Value);
            using var doc = JsonDocument.Parse(result.Value.EffectiveConfigurationJson);

            // ALPHA (Group A) is applied first (15), then BETA (Group B) overrides Group A deterministically (20)
            Assert.Equal(20, doc.RootElement.GetProperty("heartbeat").GetProperty("intervalSeconds").GetInt32());
        }

        [Fact]
        public async Task SameTarget_MultipleActiveAssignments_HigherVersionWins()
        {
            var org = new Organization { Name = "Org", Code = "ORG", Status = "Active" };
            var site = new Site { OrganizationId = org.Id, Name = "Site A", Code = "SITEA", Status = "Active" };
            var ws = new Workstation { PcId = "PC-01", Hostname = "pc-01", IpAddress = "10.0.0.1", MacAddress = "00:11:22:33:44:55", OrganizationEntityId = org.Id, SiteEntityId = site.Id };

            _siteRepoMock.Setup(r => r.GetByIdAsync(site.Id, false, It.IsAny<CancellationToken>())).ReturnsAsync(site);

            var globalTarget = ConfigurationTarget.CreateGlobal(org.Id);
            var defaultSchema = new SayraConfigurationSchema();
            defaultSchema.Heartbeat.IntervalSeconds = 10;
            defaultSchema.Heartbeat.TimeoutSeconds = 40;
            var globalPayload = _normalizer.NormalizeToJson(defaultSchema);
            var globalPkg = ConfigurationPackage.CreateFull("g-pkg", 1, globalPayload);
            var globalAssign = ConfigurationAssignment.Create(globalPkg.Id, globalTarget.Id);

            var siteTarget = ConfigurationTarget.CreateSite(org.Id, site.Id);

            var sitePayloadV1 = "{\"heartbeat\":{\"intervalSeconds\":15}}";
            var sitePayloadV2 = "{\"heartbeat\":{\"intervalSeconds\":25}}";

            var pkgV1 = ConfigurationPackage.CreateFull("site-pkg", 1, sitePayloadV1);
            var pkgV2 = ConfigurationPackage.CreateFull("site-pkg", 2, sitePayloadV2);

            var assign1 = ConfigurationAssignment.Create(pkgV1.Id, siteTarget.Id);
            var assign2 = ConfigurationAssignment.Create(pkgV2.Id, siteTarget.Id);

            SetupBasicContext(ws, org, new List<ConfigurationAssignment> { globalAssign, assign1, assign2 });
            SetupTargetAndPackage(globalTarget, globalPkg);
            SetupTargetAndPackage(siteTarget, pkgV1);
            SetupTargetAndPackage(siteTarget, pkgV2);

            var resolver = CreateResolver();
            var result = await resolver.ResolveEffectiveConfigurationAsync(ws.Id);

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.NotNull(result.Value);
            using var doc = JsonDocument.Parse(result.Value.EffectiveConfigurationJson);

            // Version 2 wins over Version 1 at the same site target scope
            Assert.Equal(25, doc.RootElement.GetProperty("heartbeat").GetProperty("intervalSeconds").GetInt32());
        }

        [Fact]
        public async Task LifecycleEligibility_InactivePackage_IsFilteredOut()
        {
            var org = new Organization { Name = "Org", Code = "ORG", Status = "Active" };
            var ws = new Workstation { PcId = "PC-01", Hostname = "pc-01", IpAddress = "10.0.0.1", MacAddress = "00:11:22:33:44:55", OrganizationEntityId = org.Id };

            var globalTarget = ConfigurationTarget.CreateGlobal(org.Id);
            var defaultSchema = new SayraConfigurationSchema();
            defaultSchema.Server.Port = 5000;
            var globalPayload = _normalizer.NormalizeToJson(defaultSchema);
            var globalPkg = ConfigurationPackage.CreateFull("global-config", 1, globalPayload);
            globalPkg.IsActive = false; // Package deactivated/revoked

            var globalAssignment = ConfigurationAssignment.Create(globalPkg.Id, globalTarget.Id);

            SetupBasicContext(ws, org, new List<ConfigurationAssignment> { globalAssignment });
            SetupTargetAndPackage(globalTarget, globalPkg);

            var resolver = CreateResolver();
            var result = await resolver.ResolveEffectiveConfigurationAsync(ws.Id);

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.NotNull(result.Value);
            // Inactive package is filtered out, returning default normalized configuration
            Assert.Empty(result.Value.AppliedSources);
        }

        [Fact]
        public async Task Security_DeactivatedWorkstation_ReturnsError()
        {
            var org = new Organization { Name = "Org", Code = "ORG", Status = "Active" };
            var ws = new Workstation { PcId = "PC-01", Hostname = "pc-01", IpAddress = "10.0.0.1", MacAddress = "00:11:22:33:44:55", OrganizationEntityId = org.Id, IsDeactivated = true };

            _workstationRepoMock.Setup(r => r.GetByIdAsync(ws.Id, false, It.IsAny<CancellationToken>())).ReturnsAsync(ws);

            var resolver = CreateResolver();
            var result = await resolver.ResolveEffectiveConfigurationAsync(ws.Id);

            Assert.False(result.IsSuccess);
            Assert.Equal("WORKSTATION_DEACTIVATED", result.ErrorCode);
        }

        [Fact]
        public async Task Determinism_IdenticalState_ProducesIdenticalResult()
        {
            var org = new Organization { Name = "Org", Code = "ORG", Status = "Active" };
            var ws = new Workstation { PcId = "PC-01", Hostname = "pc-01", IpAddress = "10.0.0.1", MacAddress = "00:11:22:33:44:55", OrganizationEntityId = org.Id };

            var globalTarget = ConfigurationTarget.CreateGlobal(org.Id);
            var defaultSchema = new SayraConfigurationSchema();
            defaultSchema.Server.Port = 5000;
            defaultSchema.Heartbeat.IntervalSeconds = 10;
            defaultSchema.Heartbeat.TimeoutSeconds = 30;

            var globalPayload = _normalizer.NormalizeToJson(defaultSchema);
            var globalPkg = ConfigurationPackage.CreateFull("global-config", 1, globalPayload);
            var globalAssignment = ConfigurationAssignment.Create(globalPkg.Id, globalTarget.Id);

            SetupBasicContext(ws, org, new List<ConfigurationAssignment> { globalAssignment });
            SetupTargetAndPackage(globalTarget, globalPkg);

            var resolver = CreateResolver();

            var res1 = await resolver.ResolveEffectiveConfigurationAsync(ws.Id);
            var res2 = await resolver.ResolveEffectiveConfigurationAsync(ws.Id);

            Assert.True(res1.IsSuccess, res1.ErrorMessage);
            Assert.True(res2.IsSuccess, res2.ErrorMessage);
            Assert.NotNull(res1.Value);
            Assert.NotNull(res2.Value);
            Assert.Equal(res1.Value.EffectiveConfigurationJson, res2.Value.EffectiveConfigurationJson);
        }

        private void SetupBasicContext(Workstation ws, Organization org, List<ConfigurationAssignment> assignments)
        {
            _workstationRepoMock.Setup(r => r.GetByIdAsync(ws.Id, false, It.IsAny<CancellationToken>())).ReturnsAsync(ws);
            _orgRepoMock.Setup(r => r.GetByIdAsync(org.Id, false, It.IsAny<CancellationToken>())).ReturnsAsync(org);
            _assignmentRepoMock.Setup(r => r.GetApplicableAssignmentsAsync(org.Id, ws.SiteEntityId, It.IsAny<List<Guid>>(), ws.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(assignments);
        }

        private void SetupTargetAndPackage(ConfigurationTarget target, ConfigurationPackage package)
        {
            _targetRepoMock.Setup(r => r.GetByIdAsync(target.Id, false, It.IsAny<CancellationToken>())).ReturnsAsync(target);
            _packageRepoMock.Setup(r => r.GetByIdAsync(package.Id, false, It.IsAny<CancellationToken>())).ReturnsAsync(package);
        }
    }
}
