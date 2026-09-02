using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Configuration;
using Sayra.Backend.Application.Configuration.Models;
using Sayra.Backend.Domain;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class ConfigurationResolverCacheTests
    {
        private readonly Mock<IRepository<Workstation>> _workstationRepoMock;
        private readonly Mock<IRepository<Organization>> _orgRepoMock;
        private readonly Mock<IRepository<Site>> _siteRepoMock;
        private readonly Mock<IWorkstationGroupRepository> _groupRepoMock;
        private readonly Mock<IConfigurationAssignmentRepository> _assignmentRepoMock;
        private readonly Mock<IConfigurationTargetRepository> _targetRepoMock;
        private readonly Mock<IConfigurationPackageRepository> _packageRepoMock;
        private readonly Mock<IConfigurationCache> _cacheMock;
        private readonly IConfigurationDeltaEngine _deltaEngine;
        private readonly IConfigurationNormalizer _normalizer;
        private readonly IConfigurationValidator _validator;

        public ConfigurationResolverCacheTests()
        {
            _workstationRepoMock = new Mock<IRepository<Workstation>>();
            _orgRepoMock = new Mock<IRepository<Organization>>();
            _siteRepoMock = new Mock<IRepository<Site>>();
            _groupRepoMock = new Mock<IWorkstationGroupRepository>();
            _assignmentRepoMock = new Mock<IConfigurationAssignmentRepository>();
            _targetRepoMock = new Mock<IConfigurationTargetRepository>();
            _packageRepoMock = new Mock<IConfigurationPackageRepository>();
            _cacheMock = new Mock<IConfigurationCache>();

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
                _validator,
                publicationRepository: null,
                configurationCache: _cacheMock.Object);
        }

        [Fact]
        public async Task ResolveEffectiveConfiguration_CacheHit_ReturnsCachedItemWithoutQueryingAssignments()
        {
            var org = new Organization { Name = "Org", Code = "ORG", Status = "Active" };
            var ws = new Workstation { PcId = "PC-01", Hostname = "pc-01", IpAddress = "10.0.0.1", MacAddress = "00:11:22:33:44:55", OrganizationEntityId = org.Id };

            _workstationRepoMock.Setup(r => r.GetByIdAsync(ws.Id, false, It.IsAny<CancellationToken>())).ReturnsAsync(ws);
            _orgRepoMock.Setup(r => r.GetByIdAsync(org.Id, false, It.IsAny<CancellationToken>())).ReturnsAsync(org);
            _groupRepoMock.Setup(r => r.GetWorkstationGroupIdsForWorkstationAsync(ws.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Guid>());

            var cachedConfig = new CachedEffectiveConfiguration
            {
                SchemaVersion = "1.0",
                EffectiveConfigurationJson = "{\"server\":{\"port\":9000}}",
                AppliedSources = new List<AppliedConfigurationSourceDto>()
            };

            _cacheMock.Setup(c => c.GetEffectiveConfigurationAsync(org.Id, ws.Id, null, It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(cachedConfig);

            var resolver = CreateResolver();
            var result = await resolver.ResolveEffectiveConfigurationAsync(ws.Id);

            Assert.True(result.IsSuccess);
            Assert.Contains("9000", result.Value.EffectiveConfigurationJson);

            // Verify assignment repo was NOT called because cache was a hit
            _assignmentRepoMock.Verify(a => a.GetApplicableAssignmentsAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<List<Guid>>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ResolveEffectiveConfiguration_CacheMiss_ResolvesFromDatabaseAndPopulatesCache()
        {
            var org = new Organization { Name = "Org", Code = "ORG", Status = "Active" };
            var ws = new Workstation { PcId = "PC-01", Hostname = "pc-01", IpAddress = "10.0.0.1", MacAddress = "00:11:22:33:44:55", OrganizationEntityId = org.Id };

            var globalTarget = ConfigurationTarget.CreateGlobal(org.Id);
            var defaultSchema = new SayraConfigurationSchema();
            defaultSchema.Server.Port = 5000;
            var globalPayload = _normalizer.NormalizeToJson(defaultSchema);
            var globalPkg = ConfigurationPackage.CreateFull("global-config", 1, globalPayload);
            var globalAssignment = ConfigurationAssignment.Create(globalPkg.Id, globalTarget.Id);

            _workstationRepoMock.Setup(r => r.GetByIdAsync(ws.Id, false, It.IsAny<CancellationToken>())).ReturnsAsync(ws);
            _orgRepoMock.Setup(r => r.GetByIdAsync(org.Id, false, It.IsAny<CancellationToken>())).ReturnsAsync(org);
            _groupRepoMock.Setup(r => r.GetWorkstationGroupIdsForWorkstationAsync(ws.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Guid>());

            _assignmentRepoMock.Setup(r => r.GetApplicableAssignmentsAsync(org.Id, null, It.IsAny<List<Guid>>(), ws.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ConfigurationAssignment> { globalAssignment });

            _targetRepoMock.Setup(r => r.GetByIdAsync(globalTarget.Id, false, It.IsAny<CancellationToken>())).ReturnsAsync(globalTarget);
            _packageRepoMock.Setup(r => r.GetByIdAsync(globalPkg.Id, false, It.IsAny<CancellationToken>())).ReturnsAsync(globalPkg);

            _cacheMock.Setup(c => c.GetEffectiveConfigurationAsync(org.Id, ws.Id, null, It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CachedEffectiveConfiguration?)null);

            var resolver = CreateResolver();
            var result = await resolver.ResolveEffectiveConfigurationAsync(ws.Id);

            Assert.True(result.IsSuccess);

            // Verify cache population was called
            _cacheMock.Verify(c => c.SetEffectiveConfigurationAsync(
                org.Id, ws.Id, null, It.IsAny<List<Guid>>(),
                It.Is<CachedEffectiveConfiguration>(cfg => cfg.EffectiveConfigurationJson.Contains("5000")),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
