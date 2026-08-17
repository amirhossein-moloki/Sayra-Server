using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Xunit;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Transport;
using Sayra.Backend.Application.Workstations;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Exceptions;
using Sayra.Backend.Shared;

namespace Sayra.Backend.UnitTests
{
    public class WorkstationUnitTests
    {
        #region Domain Tests

        [Fact]
        public void Valid_Workstation_Should_Normalize_And_Validate_Successfully()
        {
            var workstation = new Workstation
            {
                PcId = "pc-01",
                SiteId = "site-a",
                Hostname = "desktop-01",
                MacAddress = "00-11-22-33-44-55",
                IpAddress = "192.168.1.10"
            };

            workstation.NormalizeAndValidate();

            Assert.Equal("PC-01", workstation.PcId);
            Assert.Equal("SITE-A", workstation.SiteId);
            Assert.Equal("desktop-01", workstation.Hostname);
            Assert.Equal("00:11:22:33:44:55", workstation.MacAddress);
            Assert.Equal("192.168.1.10", workstation.IpAddress);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void Invalid_PcId_Should_Throw_InvalidDomainException(string? invalidPcId)
        {
            var workstation = new Workstation
            {
                PcId = invalidPcId!,
                SiteId = "SITE-A",
                Hostname = "DESKTOP-01",
                MacAddress = "00-11-22-33-44-55",
                IpAddress = "192.168.1.10"
            };

            var ex = Assert.Throws<InvalidDomainException>(() => workstation.NormalizeAndValidate());
            Assert.Equal("INVALID_PC_ID", ex.ErrorCode);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void Invalid_SiteId_Should_Throw_InvalidDomainException(string? invalidSiteId)
        {
            var workstation = new Workstation
            {
                PcId = "PC-01",
                SiteId = invalidSiteId!,
                Hostname = "DESKTOP-01",
                MacAddress = "00-11-22-33-44-55",
                IpAddress = "192.168.1.10"
            };

            var ex = Assert.Throws<InvalidDomainException>(() => workstation.NormalizeAndValidate());
            Assert.Equal("INVALID_SITE_ID", ex.ErrorCode);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void Invalid_Hostname_Should_Throw_InvalidDomainException(string? invalidHostname)
        {
            var workstation = new Workstation
            {
                PcId = "PC-01",
                SiteId = "SITE-A",
                Hostname = invalidHostname!,
                MacAddress = "00-11-22-33-44-55",
                IpAddress = "192.168.1.10"
            };

            var ex = Assert.Throws<InvalidDomainException>(() => workstation.NormalizeAndValidate());
            Assert.Equal("INVALID_HOSTNAME", ex.ErrorCode);
        }

        [Theory]
        [InlineData("00-11-22-33-44")]
        [InlineData("00:11:22:33:44:GG")]
        [InlineData("invalid-mac")]
        public void Invalid_MacAddress_Should_Throw_InvalidDomainException(string invalidMac)
        {
            var workstation = new Workstation
            {
                PcId = "PC-01",
                SiteId = "SITE-A",
                Hostname = "DESKTOP-01",
                MacAddress = invalidMac,
                IpAddress = "192.168.1.10"
            };

            var ex = Assert.Throws<InvalidDomainException>(() => workstation.NormalizeAndValidate());
            Assert.Equal("INVALID_MAC_ADDRESS", ex.ErrorCode);
        }

        [Theory]
        [InlineData("256.256.256.256")]
        [InlineData("192.168.1.300")]
        [InlineData("invalid-ip")]
        public void Invalid_IpAddress_Should_Throw_InvalidDomainException(string invalidIp)
        {
            var workstation = new Workstation
            {
                PcId = "PC-01",
                SiteId = "SITE-A",
                Hostname = "DESKTOP-01",
                MacAddress = "00-11-22-33-44-55",
                IpAddress = invalidIp
            };

            var ex = Assert.Throws<InvalidDomainException>(() => workstation.NormalizeAndValidate());
            Assert.Equal("INVALID_IP_ADDRESS", ex.ErrorCode);
        }

        [Fact]
        public void Valid_State_Transitions_Should_Succeed()
        {
            var workstation = new Workstation { Status = "OFFLINE" };

            // OFFLINE -> ONLINE
            workstation.TransitionTo("ONLINE");
            Assert.Equal("ONLINE", workstation.Status);

            // ONLINE -> IN_USE
            workstation.TransitionTo("IN_USE");
            Assert.Equal("IN_USE", workstation.Status);

            // IN_USE -> MAINTENANCE
            workstation.TransitionTo("MAINTENANCE");
            Assert.Equal("MAINTENANCE", workstation.Status);

            // MAINTENANCE -> OFFLINE
            workstation.TransitionTo("OFFLINE");
            Assert.Equal("OFFLINE", workstation.Status);
        }

        [Fact]
        public void Invalid_State_Transitions_Should_Throw_InvalidDomainException()
        {
            var w1 = new Workstation { Status = "OFFLINE" };
            var ex1 = Assert.Throws<InvalidDomainException>(() => w1.TransitionTo("IN_USE"));
            Assert.Equal("INVALID_TRANSITION", ex1.ErrorCode);

            var w2 = new Workstation { Status = "MAINTENANCE" };
            var ex2 = Assert.Throws<InvalidDomainException>(() => w2.TransitionTo("IN_USE"));
            Assert.Equal("INVALID_TRANSITION", ex2.ErrorCode);
        }

        #endregion

        #region Application Tests (Unit Mocks)

        [Fact]
        public async Task RegisterWorkstation_Should_Create_New_Workstation_Successfully()
        {
            // Arrange
            var mockRepo = new Mock<IRepository<Workstation>>();
            var mockSiteRepo = new Mock<IRepository<Site>>();
            var mockAudit = new Mock<IRepository<AuditEvent>>();
            var mockUow = new Mock<IUnitOfWork>();

            var sites = new List<Site> { new Site { OrganizationId = Guid.NewGuid(), SiteId = "SITE-ALPHA", Code = "SITE-ALPHA", Name = "Site Alpha" } };
            var workstations = new List<Workstation>();

            mockSiteRepo.Setup(s => s.FirstOrDefaultAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Site, bool>>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((System.Linq.Expressions.Expression<Func<Site, bool>> predicate, bool track, CancellationToken ct) => sites.FirstOrDefault(predicate.Compile()));

            mockRepo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Workstation, bool>>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((System.Linq.Expressions.Expression<Func<Workstation, bool>> predicate, bool track, CancellationToken ct) => workstations.FirstOrDefault(predicate.Compile()));

            mockRepo.Setup(r => r.GetAllAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(workstations);
            mockSiteRepo.Setup(s => s.GetAllAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(sites);

            var handler = new RegisterWorkstationCommandHandler(mockRepo.Object, mockSiteRepo.Object, mockAudit.Object, mockUow.Object);
            var command = new RegisterWorkstationCommand
            {
                PcId = "PC-UNIT-TEST",
                SiteId = "SITE-ALPHA",
                Hostname = "HOST-TEST",
                MacAddress = "AA-BB-CC-DD-EE-FF",
                IpAddress = "10.0.0.5",
                ClientVersion = "1.0.0",
                OsVersion = "Windows 11"
            };

            // Act
            var result = await handler.HandleAsync(command, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal("PC-UNIT-TEST", result.Value.PcId);
            Assert.Equal("OFFLINE", result.Value.Status);

            mockRepo.Verify(r => r.AddAsync(It.IsAny<Workstation>(), It.IsAny<CancellationToken>()), Times.Once);
            mockAudit.Verify(a => a.AddAsync(It.IsAny<AuditEvent>(), It.IsAny<CancellationToken>()), Times.Once);
            mockUow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task RegisterWorkstation_Duplicate_MacAddress_On_Different_PcId_Should_Fail()
        {
            // Arrange
            var mockRepo = new Mock<IRepository<Workstation>>();
            var mockSiteRepo = new Mock<IRepository<Site>>();
            var mockAudit = new Mock<IRepository<AuditEvent>>();
            var mockUow = new Mock<IUnitOfWork>();

            var existing = new Workstation
            {
                PcId = "PC-ORIGINAL",
                MacAddress = "AA:BB:CC:DD:EE:FF",
                SiteId = "SITE-ALPHA",
                Hostname = "HOST-TEST",
                IpAddress = "10.0.0.5"
            };

            var sites = new List<Site> { new Site { OrganizationId = Guid.NewGuid(), SiteId = "SITE-ALPHA", Code = "SITE-ALPHA", Name = "Site Alpha" } };
            var workstations = new List<Workstation> { existing };

            mockSiteRepo.Setup(s => s.FirstOrDefaultAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Site, bool>>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((System.Linq.Expressions.Expression<Func<Site, bool>> predicate, bool track, CancellationToken ct) => sites.FirstOrDefault(predicate.Compile()));

            mockRepo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Workstation, bool>>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((System.Linq.Expressions.Expression<Func<Workstation, bool>> predicate, bool track, CancellationToken ct) => workstations.FirstOrDefault(predicate.Compile()));

            mockRepo.Setup(r => r.GetAllAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(workstations);
            mockSiteRepo.Setup(s => s.GetAllAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(sites);

            var handler = new RegisterWorkstationCommandHandler(mockRepo.Object, mockSiteRepo.Object, mockAudit.Object, mockUow.Object);
            var command = new RegisterWorkstationCommand
            {
                PcId = "PC-NEW-DUPLICATE",
                SiteId = "SITE-ALPHA",
                Hostname = "HOST-TEST",
                MacAddress = "AA-BB-CC-DD-EE-FF",
                IpAddress = "10.0.0.6",
                ClientVersion = "1.0.0",
                OsVersion = "Windows 11"
            };

            // Act
            var result = await handler.HandleAsync(command, CancellationToken.None);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("DUPLICATE_MAC_ADDRESS", result.ErrorCode);
        }

        [Fact]
        public async Task RegisterWorkstation_Idempotent_Of_Same_PcId_Should_Update_Metadata_Succeed()
        {
            // Arrange
            var mockRepo = new Mock<IRepository<Workstation>>();
            var mockSiteRepo = new Mock<IRepository<Site>>();
            var mockAudit = new Mock<IRepository<AuditEvent>>();
            var mockUow = new Mock<IUnitOfWork>();

            var existingId = Guid.NewGuid();
            var existing = new Workstation
            {
                PcId = "PC-SAMENAME",
                MacAddress = "11:22:33:44:55:66",
                SiteId = "SITE-OLD",
                Hostname = "HOST-OLD",
                IpAddress = "10.0.0.1",
                ClientVersion = "0.9.0",
                OsVersion = "Windows 10"
            };
            typeof(BaseEntity).GetProperty("Id")?.SetValue(existing, existingId);

            var sites = new List<Site> { new Site { OrganizationId = Guid.NewGuid(), SiteId = "SITE-NEW", Code = "SITE-NEW", Name = "Site New" } };
            var workstations = new List<Workstation> { existing };

            mockSiteRepo.Setup(s => s.FirstOrDefaultAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Site, bool>>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((System.Linq.Expressions.Expression<Func<Site, bool>> predicate, bool track, CancellationToken ct) => sites.FirstOrDefault(predicate.Compile()));

            mockRepo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Workstation, bool>>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((System.Linq.Expressions.Expression<Func<Workstation, bool>> predicate, bool track, CancellationToken ct) => workstations.FirstOrDefault(predicate.Compile()));

            mockRepo.Setup(r => r.GetAllAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(workstations);
            mockRepo.Setup(r => r.GetByIdAsync(existingId, true, It.IsAny<CancellationToken>()))
                .ReturnsAsync(existing);
            mockSiteRepo.Setup(s => s.GetAllAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(sites);

            var handler = new RegisterWorkstationCommandHandler(mockRepo.Object, mockSiteRepo.Object, mockAudit.Object, mockUow.Object);
            var command = new RegisterWorkstationCommand
            {
                PcId = "PC-SAMENAME",
                SiteId = "SITE-NEW",
                Hostname = "HOST-NEW",
                MacAddress = "11:22:33:44:55:66",
                IpAddress = "10.0.0.2",
                ClientVersion = "1.0.0",
                OsVersion = "Windows 11"
            };

            // Act
            var result = await handler.HandleAsync(command, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal("SITE-NEW", result.Value.SiteId);
            Assert.Equal("HOST-NEW", result.Value.Hostname);
            Assert.Equal("10.0.0.2", result.Value.IpAddress);
            Assert.Equal("1.0.0", result.Value.ClientVersion);
            Assert.Equal("Windows 11", result.Value.OsVersion);

            mockRepo.Verify(r => r.AddAsync(It.IsAny<Workstation>(), It.IsAny<CancellationToken>()), Times.Never);
            mockRepo.Verify(r => r.Update(It.IsAny<Workstation>()), Times.Once);
            mockUow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        #endregion
    }
}
