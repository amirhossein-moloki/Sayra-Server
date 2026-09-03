using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Configuration;
using Sayra.Backend.Application.Configuration.Models;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Shared;
using Xunit;

namespace Sayra.Backend.UnitTests.Configuration
{
    public class ConfigurationSecurityAndAuditUnitTests
    {
        [Fact]
        public async Task ValidateConfiguration_ShouldRecordSecurityEventsAndMetrics_WhenValidationFails()
        {
            // Arrange
            var validatorMock = new Mock<IConfigurationValidator>();
            var securityEventMock = new Mock<ISecurityEventService>();
            var metricsMock = new Mock<IConfigurationMetrics>();

            validatorMock
                .Setup(v => v.Validate(It.IsAny<string>()))
                .Returns(ConfigurationValidationResult.Failure("server.port", "PORT_OUT_OF_RANGE", "Port must be positive."));

            var handler = new ValidateConfigurationCommandHandler(validatorMock.Object, securityEventMock.Object, metricsMock.Object);
            var command = new ValidateConfigurationCommand(RawPayload: "{\"server\":{\"port\":-1}}");

            // Act
            var result = await handler.HandleAsync(command, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.False(result.Value.IsValid);

            metricsMock.Verify(m => m.RecordValidationFailure("PORT_OUT_OF_RANGE"), Times.Once);
            securityEventMock.Verify(s => s.RecordSecurityEventAsync(
                "CONFIG_VALIDATION_FAILED",
                It.IsAny<Guid?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                It.IsAny<string?>(),
                It.IsAny<Guid?>(),
                "VALIDATE",
                "FAILED",
                It.Is<string>(r => r.Contains("Port must be positive.")),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SynchronizeConfiguration_ShouldRejectDisabledWorkstation_AndRecordSecurityEvent()
        {
            // Arrange
            var wsRepoMock = new Mock<IRepository<Workstation>>();
            var resolverMock = new Mock<IConfigurationResolver>();
            var signingMock = new Mock<IConfigurationSigningService>();
            var hashMock = new Mock<IConfigurationHashService>();
            var canonicalMock = new Mock<ICanonicalConfigurationSerializer>();
            var packageRepoMock = new Mock<IConfigurationPackageRepository>();
            var deltaMock = new Mock<IConfigurationDeltaEngine>();
            var loggerMock = new Mock<ILogger<SynchronizeConfigurationQueryHandler>>();
            var securityEventMock = new Mock<ISecurityEventService>();
            var metricsMock = new Mock<IConfigurationMetrics>();

            var disabledWs = new Workstation
            {
                Name = "WS-01",
                PcId = "PC-DISABLED",
                SiteId = "SITE-01",
                Hostname = "host-01",
                IpAddress = "127.0.0.1",
                MacAddress = "11:22:33:44:55:66",
                IsDisabled = true
            };

            wsRepoMock
                .Setup(r => r.GetByIdAsync(disabledWs.Id, false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(disabledWs);

            var handler = new SynchronizeConfigurationQueryHandler(
                wsRepoMock.Object,
                resolverMock.Object,
                signingMock.Object,
                hashMock.Object,
                canonicalMock.Object,
                packageRepoMock.Object,
                deltaMock.Object,
                loggerMock.Object,
                securityEventMock.Object,
                metricsMock.Object);

            var query = new SynchronizeConfigurationQuery("PC-DISABLED", 1, disabledWs.Id, null);

            // Act
            var result = await handler.HandleAsync(query, CancellationToken.None);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("WORKSTATION_DISABLED", result.ErrorCode);

            metricsMock.Verify(m => m.RecordSecurityDenied("sync", "WORKSTATION_DISABLED"), Times.Once);
            securityEventMock.Verify(s => s.RecordSecurityEventAsync(
                "CONFIG_ACCESS_DENIED",
                It.IsAny<Guid?>(),
                "Workstation",
                "PC-DISABLED",
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                "Workstation",
                disabledWs.Id,
                "SYNC",
                "DENIED",
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SynchronizeConfiguration_ShouldRejectCrossOrganizationAccess_AndRecordSecurityEvent()
        {
            // Arrange
            var wsRepoMock = new Mock<IRepository<Workstation>>();
            var resolverMock = new Mock<IConfigurationResolver>();
            var signingMock = new Mock<IConfigurationSigningService>();
            var hashMock = new Mock<IConfigurationHashService>();
            var canonicalMock = new Mock<ICanonicalConfigurationSerializer>();
            var packageRepoMock = new Mock<IConfigurationPackageRepository>();
            var deltaMock = new Mock<IConfigurationDeltaEngine>();
            var loggerMock = new Mock<ILogger<SynchronizeConfigurationQueryHandler>>();
            var securityEventMock = new Mock<ISecurityEventService>();
            var metricsMock = new Mock<IConfigurationMetrics>();

            Guid orgA = Guid.NewGuid();
            Guid orgB = Guid.NewGuid();

            var ws = new Workstation
            {
                Name = "WS-02",
                PcId = "PC-ORG-A",
                SiteId = "SITE-01",
                Hostname = "host-02",
                IpAddress = "127.0.0.1",
                MacAddress = "11:22:33:44:55:77",
                OrganizationEntityId = orgA
            };

            wsRepoMock
                .Setup(r => r.GetByIdAsync(ws.Id, false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(ws);

            var handler = new SynchronizeConfigurationQueryHandler(
                wsRepoMock.Object,
                resolverMock.Object,
                signingMock.Object,
                hashMock.Object,
                canonicalMock.Object,
                packageRepoMock.Object,
                deltaMock.Object,
                loggerMock.Object,
                securityEventMock.Object,
                metricsMock.Object);

            var query = new SynchronizeConfigurationQuery("PC-ORG-A", 1, ws.Id, orgB);

            // Act
            var result = await handler.HandleAsync(query, CancellationToken.None);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("CROSS_ORGANIZATION_ACCESS_DENIED", result.ErrorCode);

            metricsMock.Verify(m => m.RecordSecurityDenied("sync", "CROSS_ORGANIZATION_ACCESS_DENIED"), Times.Once);
            securityEventMock.Verify(s => s.RecordSecurityEventAsync(
                "CONFIG_SECURITY_VIOLATION",
                It.IsAny<Guid?>(),
                "Workstation",
                "PC-ORG-A",
                orgB,
                It.IsAny<Guid?>(),
                "Organization",
                orgB,
                "SYNC",
                "DENIED",
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task PreparePublication_ShouldRecordAudit_WhenPreparedSuccessfully()
        {
            // Arrange
            var pkgRepoMock = new Mock<IConfigurationPackageRepository>();
            var targetRepoMock = new Mock<IConfigurationTargetRepository>();
            var assignRepoMock = new Mock<IConfigurationAssignmentRepository>();
            var pubRepoMock = new Mock<IConfigurationPublicationRepository>();
            var signingMock = new Mock<IConfigurationSigningService>();
            var validatorMock = new Mock<IConfigurationValidator>();
            var uowMock = new Mock<IUnitOfWork>();
            var securityEventMock = new Mock<ISecurityEventService>();

            Guid orgId = Guid.NewGuid();
            var target = ConfigurationTarget.CreateGlobal(orgId);
            var package = ConfigurationPackage.CreateFull("default", 1, "{\"version\":\"1.0\"}", "1.0", "admin");
            package.SetCryptographicSignature("hash123", "sig123", "RSA-SHA256", "key1");

            pkgRepoMock.Setup(r => r.GetByIdAsync(package.Id, false, It.IsAny<CancellationToken>())).ReturnsAsync(package);
            targetRepoMock.Setup(r => r.GetByIdAsync(target.Id, false, It.IsAny<CancellationToken>())).ReturnsAsync(target);
            signingMock.Setup(s => s.VerifyPackageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ConfigurationVerificationResult.Success("key1"));
            validatorMock.Setup(v => v.Validate(It.IsAny<string>())).Returns(ConfigurationValidationResult.Success());

            var handler = new PreparePublicationCommandHandler(
                pkgRepoMock.Object,
                targetRepoMock.Object,
                assignRepoMock.Object,
                pubRepoMock.Object,
                signingMock.Object,
                validatorMock.Object,
                uowMock.Object,
                null,
                securityEventMock.Object);

            var command = new PreparePublicationCommand(package.Id, target.Id, "admin", "Notes");

            // Act
            var result = await handler.HandleAsync(command, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess);
            securityEventMock.Verify(s => s.RecordSecurityEventAsync(
                "CONFIG_CREATED",
                It.IsAny<Guid?>(),
                "admin",
                It.IsAny<string?>(),
                orgId,
                It.IsAny<Guid?>(),
                "ConfigurationPublication",
                result.Value.Id,
                "PREPARE_PUBLICATION",
                "SUCCESS",
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
