using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Configuration;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Exceptions;
using Sayra.Backend.Shared;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class ConfigurationTargetingUnitTests
    {
        private readonly Mock<IWorkstationGroupRepository> _groupRepoMock;
        private readonly Mock<IConfigurationTargetRepository> _targetRepoMock;
        private readonly Mock<IConfigurationAssignmentRepository> _assignmentRepoMock;
        private readonly Mock<IConfigurationPackageRepository> _packageRepoMock;
        private readonly Mock<IRepository<Organization>> _orgRepoMock;
        private readonly Mock<IRepository<Site>> _siteRepoMock;
        private readonly Mock<IRepository<Workstation>> _workstationRepoMock;
        private readonly Mock<IUnitOfWork> _unitOfWorkMock;

        public ConfigurationTargetingUnitTests()
        {
            _groupRepoMock = new Mock<IWorkstationGroupRepository>();
            _targetRepoMock = new Mock<IConfigurationTargetRepository>();
            _assignmentRepoMock = new Mock<IConfigurationAssignmentRepository>();
            _packageRepoMock = new Mock<IConfigurationPackageRepository>();
            _orgRepoMock = new Mock<IRepository<Organization>>();
            _siteRepoMock = new Mock<IRepository<Site>>();
            _workstationRepoMock = new Mock<IRepository<Workstation>>();
            _unitOfWorkMock = new Mock<IUnitOfWork>();
        }

        // --- Domain Invariant Tests ---

        [Fact]
        public void ConfigurationTarget_CreateGlobal_ValidatesScopes()
        {
            var orgId = Guid.NewGuid();
            var target = ConfigurationTarget.CreateGlobal(orgId);

            Assert.Equal(ConfigurationTargetType.Global, target.TargetType);
            Assert.Equal(orgId, target.OrganizationId);
            Assert.Null(target.SiteId);
            Assert.Null(target.GroupId);
            Assert.Null(target.WorkstationId);
        }

        [Fact]
        public void ConfigurationTarget_CreateSite_RequiresSiteId()
        {
            var orgId = Guid.NewGuid();
            var siteId = Guid.NewGuid();

            var target = ConfigurationTarget.CreateSite(orgId, siteId);

            Assert.Equal(ConfigurationTargetType.Site, target.TargetType);
            Assert.Equal(orgId, target.OrganizationId);
            Assert.Equal(siteId, target.SiteId);
            Assert.Null(target.GroupId);
            Assert.Null(target.WorkstationId);

            Assert.Throws<InvalidDomainException>(() => ConfigurationTarget.CreateSite(orgId, Guid.Empty));
        }

        [Fact]
        public void ConfigurationTarget_CreateGroup_RequiresGroupId()
        {
            var orgId = Guid.NewGuid();
            var groupId = Guid.NewGuid();

            var target = ConfigurationTarget.CreateGroup(orgId, groupId);

            Assert.Equal(ConfigurationTargetType.Group, target.TargetType);
            Assert.Equal(orgId, target.OrganizationId);
            Assert.Equal(groupId, target.GroupId);
            Assert.Null(target.WorkstationId);

            Assert.Throws<InvalidDomainException>(() => ConfigurationTarget.CreateGroup(orgId, Guid.Empty));
        }

        [Fact]
        public void ConfigurationTarget_CreateWorkstation_RequiresWorkstationId()
        {
            var orgId = Guid.NewGuid();
            var workstationId = Guid.NewGuid();

            var target = ConfigurationTarget.CreateWorkstation(orgId, workstationId);

            Assert.Equal(ConfigurationTargetType.Workstation, target.TargetType);
            Assert.Equal(orgId, target.OrganizationId);
            Assert.Equal(workstationId, target.WorkstationId);

            Assert.Throws<InvalidDomainException>(() => ConfigurationTarget.CreateWorkstation(orgId, Guid.Empty));
        }

        [Fact]
        public void ConfigurationTarget_InvalidCombinations_ThrowsInvalidDomainException()
        {
            var orgId = Guid.NewGuid();
            var siteId = Guid.NewGuid();

            // Global with site ID
            var target = new ConfigurationTarget
            {
                TargetType = ConfigurationTargetType.Global,
                OrganizationId = orgId,
                SiteId = siteId
            };

            Assert.Throws<InvalidDomainException>(() => target.NormalizeAndValidate());
        }

        [Fact]
        public void ConfigurationAssignment_CreateAndUnassign_Lifecycle()
        {
            var pkgId = Guid.NewGuid();
            var targetId = Guid.NewGuid();

            var assignment = ConfigurationAssignment.Create(pkgId, targetId, "admin");

            Assert.True(assignment.IsActive);
            Assert.Equal(pkgId, assignment.ConfigurationPackageId);
            Assert.Equal(targetId, assignment.ConfigurationTargetId);

            assignment.Unassign();
            Assert.False(assignment.IsActive);
            Assert.NotNull(assignment.UpdatedAt);

            assignment.Reassign("admin2");
            Assert.True(assignment.IsActive);
            Assert.Equal("admin2", assignment.AssignedBy);
        }

        // --- Application Handler Tests ---

        [Fact]
        public async Task CreateConfigurationTargetCommand_CrossOrganizationSite_IsRejected()
        {
            var orgA = Guid.NewGuid();
            var orgB = Guid.NewGuid();
            var siteInOrgB = Guid.NewGuid();

            var activeOrgA = new Organization { Name = "Org A", Code = "ORGA", Status = "Active" };
            var siteInB = new Site { OrganizationId = orgB, Name = "Site B", Code = "SITEB", Status = "Active" };

            _orgRepoMock.Setup(r => r.GetByIdAsync(orgA, false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(activeOrgA);
            _siteRepoMock.Setup(r => r.GetByIdAsync(siteInOrgB, false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(siteInB);

            var handler = new CreateConfigurationTargetCommandHandler(
                _targetRepoMock.Object, _orgRepoMock.Object, _siteRepoMock.Object,
                _groupRepoMock.Object, _workstationRepoMock.Object, _unitOfWorkMock.Object);

            var command = new CreateConfigurationTargetCommand
            {
                TargetType = ConfigurationTargetType.Site,
                OrganizationId = orgA,
                SiteId = siteInOrgB
            };

            var result = await handler.HandleAsync(command);

            Assert.False(result.IsSuccess);
            Assert.Equal("CROSS_ORGANIZATION_TARGET_REJECTED", result.ErrorCode);
        }

        [Fact]
        public async Task CreateConfigurationTargetCommand_CrossOrganizationWorkstation_IsRejected()
        {
            var orgA = Guid.NewGuid();
            var orgB = Guid.NewGuid();
            var wsInOrgB = Guid.NewGuid();

            var activeOrgA = new Organization { Name = "Org A", Code = "ORGA", Status = "Active" };
            var wsInB = new Workstation { PcId = "PC-B", Hostname = "pc-b", IpAddress = "10.0.0.1", MacAddress = "00:11:22:33:44:55", OrganizationEntityId = orgB };

            _orgRepoMock.Setup(r => r.GetByIdAsync(orgA, false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(activeOrgA);
            _workstationRepoMock.Setup(r => r.GetByIdAsync(wsInOrgB, false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(wsInB);

            var handler = new CreateConfigurationTargetCommandHandler(
                _targetRepoMock.Object, _orgRepoMock.Object, _siteRepoMock.Object,
                _groupRepoMock.Object, _workstationRepoMock.Object, _unitOfWorkMock.Object);

            var command = new CreateConfigurationTargetCommand
            {
                TargetType = ConfigurationTargetType.Workstation,
                OrganizationId = orgA,
                WorkstationId = wsInOrgB
            };

            var result = await handler.HandleAsync(command);

            Assert.False(result.IsSuccess);
            Assert.Equal("CROSS_ORGANIZATION_TARGET_REJECTED", result.ErrorCode);
        }

        [Fact]
        public async Task AssignConfigurationToTargetCommand_DuplicateActiveAssignment_IsRejected()
        {
            var pkgId = Guid.NewGuid();
            var targetId = Guid.NewGuid();

            var package = ConfigurationPackage.CreateFull("default", 1, "{}");
            var target = ConfigurationTarget.CreateGlobal(Guid.NewGuid());
            var existingAssignment = ConfigurationAssignment.Create(pkgId, targetId, "admin");

            _packageRepoMock.Setup(r => r.GetByIdAsync(pkgId, false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(package);
            _targetRepoMock.Setup(r => r.GetByIdAsync(targetId, false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(target);
            _assignmentRepoMock.Setup(r => r.GetAssignmentByPackageAndTargetAsync(pkgId, targetId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(existingAssignment);

            var handler = new AssignConfigurationToTargetCommandHandler(
                _assignmentRepoMock.Object, _packageRepoMock.Object, _targetRepoMock.Object, _unitOfWorkMock.Object);

            var command = new AssignConfigurationToTargetCommand
            {
                ConfigurationPackageId = pkgId,
                ConfigurationTargetId = targetId,
                AssignedBy = "admin"
            };

            var result = await handler.HandleAsync(command);

            Assert.False(result.IsSuccess);
            Assert.Equal("DUPLICATE_ASSIGNMENT", result.ErrorCode);
        }

        [Fact]
        public async Task AssignAndUnassignConfiguration_DoesNotModifyImmutablePackage()
        {
            var pkgId = Guid.NewGuid();
            var targetId = Guid.NewGuid();

            var package = ConfigurationPackage.CreateFull("default", 1, "{\"k\":\"v\"}");
            var originalContent = package.Content;
            var target = ConfigurationTarget.CreateGlobal(Guid.NewGuid());

            _packageRepoMock.Setup(r => r.GetByIdAsync(pkgId, false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(package);
            _targetRepoMock.Setup(r => r.GetByIdAsync(targetId, false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(target);

            var assignHandler = new AssignConfigurationToTargetCommandHandler(
                _assignmentRepoMock.Object, _packageRepoMock.Object, _targetRepoMock.Object, _unitOfWorkMock.Object);

            var assignResult = await assignHandler.HandleAsync(new AssignConfigurationToTargetCommand
            {
                ConfigurationPackageId = pkgId,
                ConfigurationTargetId = targetId,
                AssignedBy = "admin"
            });

            Assert.True(assignResult.IsSuccess);
            Assert.Equal(originalContent, package.Content);

            var assignment = ConfigurationAssignment.Create(pkgId, targetId);
            _assignmentRepoMock.Setup(r => r.GetByIdAsync(assignment.Id, true, It.IsAny<CancellationToken>()))
                .ReturnsAsync(assignment);

            var unassignHandler = new UnassignConfigurationFromTargetCommandHandler(
                _assignmentRepoMock.Object, _unitOfWorkMock.Object);

            var unassignResult = await unassignHandler.HandleAsync(new UnassignConfigurationFromTargetCommand
            {
                ConfigurationAssignmentId = assignment.Id
            });

            Assert.True(unassignResult.IsSuccess);
            Assert.False(assignment.IsActive);
            Assert.Equal(originalContent, package.Content);
        }

        [Fact]
        public async Task GetApplicableAssignmentsForWorkstation_ReturnsAllScopes()
        {
            var orgId = Guid.NewGuid();
            var siteId = Guid.NewGuid();
            var groupId = Guid.NewGuid();

            var ws = new Workstation
            {
                PcId = "PC-01",
                Hostname = "pc-01",
                IpAddress = "192.168.1.50",
                MacAddress = "00:11:22:33:44:55",
                OrganizationEntityId = orgId,
                SiteEntityId = siteId
            };
            var wsId = ws.Id;

            _workstationRepoMock.Setup(r => r.GetByIdAsync(wsId, false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(ws);

            _groupRepoMock.Setup(r => r.GetWorkstationGroupIdsForWorkstationAsync(wsId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Guid> { groupId });

            var globalTarget = ConfigurationTarget.CreateGlobal(orgId);
            var siteTarget = ConfigurationTarget.CreateSite(orgId, siteId);
            var groupTarget = ConfigurationTarget.CreateGroup(orgId, groupId, siteId);
            var wsTarget = ConfigurationTarget.CreateWorkstation(orgId, wsId, siteId, groupId);

            var pkg1 = ConfigurationPackage.CreateFull("global-pkg", 1, "{}");
            var pkg2 = ConfigurationPackage.CreateFull("site-pkg", 2, "{}");
            var pkg3 = ConfigurationPackage.CreateFull("group-pkg", 3, "{}");
            var pkg4 = ConfigurationPackage.CreateFull("ws-pkg", 4, "{}");

            var assign1 = ConfigurationAssignment.Create(pkg1.Id, globalTarget.Id);
            var assign2 = ConfigurationAssignment.Create(pkg2.Id, siteTarget.Id);
            var assign3 = ConfigurationAssignment.Create(pkg3.Id, groupTarget.Id);
            var assign4 = ConfigurationAssignment.Create(pkg4.Id, wsTarget.Id);

            _assignmentRepoMock.Setup(r => r.GetApplicableAssignmentsAsync(orgId, siteId, It.IsAny<List<Guid>>(), wsId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ConfigurationAssignment> { assign1, assign2, assign3, assign4 });

            _targetRepoMock.Setup(r => r.GetByIdAsync(globalTarget.Id, false, It.IsAny<CancellationToken>())).ReturnsAsync(globalTarget);
            _targetRepoMock.Setup(r => r.GetByIdAsync(siteTarget.Id, false, It.IsAny<CancellationToken>())).ReturnsAsync(siteTarget);
            _targetRepoMock.Setup(r => r.GetByIdAsync(groupTarget.Id, false, It.IsAny<CancellationToken>())).ReturnsAsync(groupTarget);
            _targetRepoMock.Setup(r => r.GetByIdAsync(wsTarget.Id, false, It.IsAny<CancellationToken>())).ReturnsAsync(wsTarget);

            _packageRepoMock.Setup(r => r.GetByIdAsync(pkg1.Id, false, It.IsAny<CancellationToken>())).ReturnsAsync(pkg1);
            _packageRepoMock.Setup(r => r.GetByIdAsync(pkg2.Id, false, It.IsAny<CancellationToken>())).ReturnsAsync(pkg2);
            _packageRepoMock.Setup(r => r.GetByIdAsync(pkg3.Id, false, It.IsAny<CancellationToken>())).ReturnsAsync(pkg3);
            _packageRepoMock.Setup(r => r.GetByIdAsync(pkg4.Id, false, It.IsAny<CancellationToken>())).ReturnsAsync(pkg4);

            var queryHandler = new GetApplicableAssignmentsForWorkstationQueryHandler(
                _workstationRepoMock.Object, _groupRepoMock.Object, _assignmentRepoMock.Object, _targetRepoMock.Object, _packageRepoMock.Object);

            var queryResult = await queryHandler.HandleAsync(new GetApplicableAssignmentsForWorkstationQuery { WorkstationId = wsId });

            Assert.True(queryResult.IsSuccess);
            Assert.NotNull(queryResult.Value);
            Assert.Equal(4, queryResult.Value.Count);
            Assert.Contains(queryResult.Value, a => a.TargetType == "Global");
            Assert.Contains(queryResult.Value, a => a.TargetType == "Site");
            Assert.Contains(queryResult.Value, a => a.TargetType == "Group");
            Assert.Contains(queryResult.Value, a => a.TargetType == "Workstation");
        }
    }
}
