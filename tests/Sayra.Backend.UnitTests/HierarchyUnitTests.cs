using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Xunit;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Locations;
using Sayra.Backend.Application.Organizations;
using Sayra.Backend.Application.Workstations;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Exceptions;

namespace Sayra.Backend.UnitTests
{
    public class HierarchyUnitTests
    {
        [Fact]
        public void Organization_Normalization_And_Validation_Should_Succeed()
        {
            var org = new Organization
            {
                Code = " org-code ",
                Name = " Main Organization "
            };

            org.NormalizeAndValidate();

            Assert.Equal("ORG-CODE", org.Code);
            Assert.Equal("Main Organization", org.Name);
            Assert.Equal("Active", org.Status);
            Assert.True(org.CanOperate());
        }

        [Fact]
        public void Organization_Invalid_Code_Should_Throw_InvalidDomainException()
        {
            var org = new Organization { Code = "", Name = "Org" };
            var ex = Assert.Throws<InvalidDomainException>(() => org.NormalizeAndValidate());
            Assert.Equal("INVALID_ORGANIZATION_CODE", ex.ErrorCode);
        }

        [Fact]
        public void Site_Normalization_And_Validation_Should_Succeed()
        {
            var site = new Site
            {
                OrganizationId = Guid.NewGuid(),
                Code = " site-01 ",
                Name = " Main Site ",
                Timezone = "UTC"
            };

            site.NormalizeAndValidate();

            Assert.Equal("SITE-01", site.Code);
            Assert.Equal("Main Site", site.Name);
            Assert.Equal("Active", site.Status);
            Assert.True(site.CanOperate());
        }

        [Fact]
        public void Site_Invalid_OrganizationId_Should_Throw_InvalidDomainException()
        {
            var site = new Site { OrganizationId = Guid.Empty, Code = "SITE-1", Name = "Site" };
            var ex = Assert.Throws<InvalidDomainException>(() => site.NormalizeAndValidate());
            Assert.Equal("INVALID_ORGANIZATION_ID", ex.ErrorCode);
        }

        [Fact]
        public void Zone_Normalization_And_Validation_Should_Succeed()
        {
            var zone = new Zone
            {
                SiteId = Guid.NewGuid(),
                Code = " vip-zone ",
                Name = " VIP Gaming Room "
            };

            zone.NormalizeAndValidate();

            Assert.Equal("VIP-ZONE", zone.Code);
            Assert.Equal("VIP Gaming Room", zone.Name);
            Assert.Equal("Active", zone.Status);
            Assert.True(zone.CanOperate());
        }

        [Fact]
        public async Task CreateOrganization_Duplicate_Code_Should_Fail()
        {
            var mockOrgRepo = new Mock<IRepository<Organization>>();
            var mockAuditRepo = new Mock<IRepository<AuditEvent>>();
            var mockUow = new Mock<IUnitOfWork>();

            var existing = new Organization { Code = "ORG-ALPHA", Name = "Alpha" };
            mockOrgRepo.Setup(r => r.GetAllAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Organization> { existing });

            var handler = new CreateOrganizationCommandHandler(mockOrgRepo.Object, mockAuditRepo.Object, mockUow.Object);
            var command = new CreateOrganizationCommand { Code = "org-alpha", Name = "Duplicate Alpha" };

            var result = await handler.HandleAsync(command, CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal("DUPLICATE_ORGANIZATION_CODE", result.ErrorCode);
        }

        [Fact]
        public async Task AssignWorkstation_To_Inactive_Site_Should_Fail()
        {
            var mockWsRepo = new Mock<IRepository<Workstation>>();
            var mockOrgRepo = new Mock<IRepository<Organization>>();
            var mockSiteRepo = new Mock<IRepository<Site>>();
            var mockZoneRepo = new Mock<IRepository<Zone>>();
            var mockAuditRepo = new Mock<IRepository<AuditEvent>>();
            var mockUow = new Mock<IUnitOfWork>();

            var wsId = Guid.NewGuid();
            var orgId = Guid.NewGuid();
            var siteId = Guid.NewGuid();
            var zoneId = Guid.NewGuid();

            var workstation = new Workstation { PcId = "PC-01", SiteId = "SITE-ALPHA" };
            typeof(BaseEntity).GetProperty("Id")?.SetValue(workstation, wsId);

            var org = new Organization { Status = "Active", Name = "Org" };
            typeof(BaseEntity).GetProperty("Id")?.SetValue(org, orgId);

            var site = new Site { OrganizationId = orgId, Code = "SITE-ALPHA", Name = "Site", Status = "Inactive" }; // INACTIVE SITE
            typeof(BaseEntity).GetProperty("Id")?.SetValue(site, siteId);

            var zone = new Zone { SiteId = siteId, Code = "ZONE-A", Name = "Zone", Status = "Active" };
            typeof(BaseEntity).GetProperty("Id")?.SetValue(zone, zoneId);

            mockWsRepo.Setup(r => r.GetByIdAsync(wsId, true, It.IsAny<CancellationToken>())).ReturnsAsync(workstation);
            mockOrgRepo.Setup(r => r.GetByIdAsync(orgId, false, It.IsAny<CancellationToken>())).ReturnsAsync(org);
            mockSiteRepo.Setup(r => r.GetByIdAsync(siteId, false, It.IsAny<CancellationToken>())).ReturnsAsync(site);
            mockZoneRepo.Setup(r => r.GetByIdAsync(zoneId, false, It.IsAny<CancellationToken>())).ReturnsAsync(zone);

            var handler = new AssignWorkstationCommandHandler(
                mockWsRepo.Object,
                mockOrgRepo.Object,
                mockSiteRepo.Object,
                mockZoneRepo.Object,
                mockAuditRepo.Object,
                mockUow.Object);

            var command = new AssignWorkstationCommand
            {
                WorkstationId = wsId,
                OrganizationId = orgId,
                SiteId = siteId,
                ZoneId = zoneId
            };

            var result = await handler.HandleAsync(command, CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal("SITE_INACTIVE", result.ErrorCode);
        }
    }
}
