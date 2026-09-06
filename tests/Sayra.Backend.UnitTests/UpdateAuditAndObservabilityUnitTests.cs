using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using Sayra.Backend.Api.Controllers;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Security;
using Sayra.Backend.Application.Updates;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Infrastructure.Diagnostics;
using Sayra.Backend.Infrastructure.Updates;
using Sayra.Backend.Shared;
using Xunit;

#nullable enable

namespace Sayra.Backend.UnitTests
{
    public class UpdateAuditAndObservabilityUnitTests
    {
        private readonly Guid _orgId = Guid.NewGuid();
        private readonly Guid _userId = Guid.NewGuid();

        private UserPrincipal CreateAdminPrincipal(Guid? orgId = null)
        {
            return new UserPrincipal
            {
                UserId = _userId,
                Username = "admin_user",
                OrganizationId = orgId ?? _orgId,
                IsAuthenticated = true,
                Roles = new List<string> { "Administrator" },
                Permissions = new List<string> { PermissionCatalog.ManageUpdates, PermissionCatalog.ViewUpdates }
            };
        }

        [Fact]
        public void UpdateMetrics_ShouldIncrementCounters_WithoutThrowing()
        {
            // Arrange
            var metrics = new UpdateMetrics();

            // Act & Assert (verify no exceptions thrown when recording bounded labels)
            metrics.RecordManifestRequest("available", "none");
            metrics.RecordManifestRequest("unavailable", "CLIENT_ALREADY_CURRENT");
            metrics.RecordDownloadStarted("full");
            metrics.RecordDownloadStarted("range");
            metrics.RecordDownloadCompleted("full", 1024);
            metrics.RecordDownloadCompleted("range", 512);
            metrics.RecordDownloadFailed("storage_error", "full");
            metrics.RecordDownloadCancelled("range");
            metrics.RecordDownloadInvalidRange();
            metrics.RecordReleaseOperation("publish", "success");
            metrics.RecordReleaseOperation("revoke", "success");
            metrics.RecordPackageOperation("upload_and_validate", "success");
            metrics.RecordPackageOperation("sign", "success");
            metrics.RecordInfrastructureError("storage", "io_exception");

            Assert.NotNull(metrics.ActivitySource);
        }

        [Fact]
        public async Task UpdateStorageHealthCheck_ShouldReturnHealthy_WhenProbeSucceeds()
        {
            // Arrange
            var mockStorage = new Mock<IUpdateArtifactStorage>();
            mockStorage.Setup(s => s.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            var healthCheck = new UpdateStorageHealthCheck(mockStorage.Object);
            var context = new HealthCheckContext();

            // Act
            var result = await healthCheck.CheckHealthAsync(context);

            // Assert
            Assert.Equal(HealthStatus.Healthy, result.Status);
            Assert.Contains("online", result.Description);
        }

        [Fact]
        public async Task UpdateStorageHealthCheck_ShouldReturnUnhealthy_WhenStorageThrows()
        {
            // Arrange
            var mockStorage = new Mock<IUpdateArtifactStorage>();
            mockStorage.Setup(s => s.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException("Disk read error"));

            var healthCheck = new UpdateStorageHealthCheck(mockStorage.Object);
            var context = new HealthCheckContext();

            // Act
            var result = await healthCheck.CheckHealthAsync(context);

            // Assert
            Assert.Equal(HealthStatus.Unhealthy, result.Status);
            Assert.Contains("failed", result.Description);
        }

        [Fact]
        public async Task UpdateSigningHealthCheck_ShouldReturnHealthy_WhenActiveKeyExists()
        {
            // Arrange
            var mockKeyProvider = new Mock<IUpdateSigningKeyProvider>();
            var key = ConfigurationSigningKey.Create("key-2025-v1", "-----BEGIN PUBLIC KEY-----\nMIIB...", "RSA-256");
            mockKeyProvider.Setup(p => p.GetActiveKeyAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(key);

            var healthCheck = new UpdateSigningHealthCheck(mockKeyProvider.Object);
            var context = new HealthCheckContext();

            // Act
            var result = await healthCheck.CheckHealthAsync(context);

            // Assert
            Assert.Equal(HealthStatus.Healthy, result.Status);
            Assert.Contains("key-2025-v1", result.Description);
        }

        [Fact]
        public async Task UpdateSigningHealthCheck_ShouldReturnUnhealthy_WhenActiveKeyNullOrThrows()
        {
            // Arrange
            var mockKeyProvider = new Mock<IUpdateSigningKeyProvider>();
            mockKeyProvider.Setup(p => p.GetActiveKeyAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Key store unreachable"));

            var healthCheck = new UpdateSigningHealthCheck(mockKeyProvider.Object);
            var context = new HealthCheckContext();

            // Act
            var result = await healthCheck.CheckHealthAsync(context);

            // Assert
            Assert.Equal(HealthStatus.Unhealthy, result.Status);
            Assert.Contains("failed", result.Description);
        }

        [Fact]
        public async Task GetUpdateOperationalStatusQuery_ShouldReturnStatusSummary()
        {
            // Arrange
            var releaseRepo = new Mock<IUpdateReleaseRepository>();
            var targetRepo = new Mock<IUpdateTargetRepository>();
            var storage = new Mock<IUpdateArtifactStorage>();
            var keyProvider = new Mock<IUpdateSigningKeyProvider>();
            var authService = new Mock<IAuthorizationService>();

            authService.Setup(a => a.AuthorizeAsync(It.IsAny<UserPrincipal>(), PermissionCatalog.ViewUpdates, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(AuthorizationResult.Allowed());

            storage.Setup(s => s.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            var key = ConfigurationSigningKey.Create("key-101", "PUBKEY", "RSA-256");
            keyProvider.Setup(k => k.GetActiveKeyAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(key);

            var release = UpdateRelease.Create(_orgId, "1.2.0.0", UpdateReleaseType.Standard, "Notes", "admin");
            release.TransitionTo(UpdateReleaseStatus.Validated);
            release.TransitionTo(UpdateReleaseStatus.Ready);
            release.TransitionTo(UpdateReleaseStatus.Published);
            release.TransitionTo(UpdateReleaseStatus.Active);

            releaseRepo.Setup(r => r.GetByOrganizationIdAsync(_orgId, false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<UpdateRelease> { release });

            targetRepo.Setup(t => t.GetByOrganizationIdAsync(_orgId, false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<UpdateTarget>());

            var handler = new GetUpdateOperationalStatusQueryHandler(
                releaseRepo.Object,
                targetRepo.Object,
                storage.Object,
                keyProvider.Object,
                authService.Object);

            var query = new GetUpdateOperationalStatusQuery
            {
                OrganizationId = _orgId,
                Principal = CreateAdminPrincipal()
            };

            // Act
            var result = await handler.HandleAsync(query, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal("SoftwareUpdatePlatform", result.Value.Subsystem);
            Assert.Equal("Healthy", result.Value.OverallStatus);
            Assert.Equal("Healthy", result.Value.Storage.Status);
            Assert.Equal("Healthy", result.Value.SigningProvider.Status);
            Assert.Equal("key-101", result.Value.SigningProvider.ActiveKeyId);
            Assert.NotNull(result.Value.ActiveRelease);
            Assert.Equal("1.2.0.0", result.Value.ActiveRelease.Version);
            Assert.Equal(1, result.Value.TotalReleasesCount);
        }

        [Fact]
        public async Task GetUpdateOperationalStatusQuery_ShouldDeny_CrossOrganizationAccess()
        {
            // Arrange
            var releaseRepo = new Mock<IUpdateReleaseRepository>();
            var targetRepo = new Mock<IUpdateTargetRepository>();
            var storage = new Mock<IUpdateArtifactStorage>();
            var keyProvider = new Mock<IUpdateSigningKeyProvider>();
            var authService = new Mock<IAuthorizationService>();

            authService.Setup(a => a.AuthorizeAsync(It.IsAny<UserPrincipal>(), PermissionCatalog.ViewUpdates, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(AuthorizationResult.Allowed());

            var handler = new GetUpdateOperationalStatusQueryHandler(
                releaseRepo.Object,
                targetRepo.Object,
                storage.Object,
                keyProvider.Object,
                authService.Object);

            var query = new GetUpdateOperationalStatusQuery
            {
                OrganizationId = Guid.NewGuid(), // Different organization!
                Principal = CreateAdminPrincipal(_orgId)
            };

            // Act
            var result = await handler.HandleAsync(query, CancellationToken.None);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("CROSS_ORGANIZATION_ACCESS_DENIED", result.ErrorCode);
        }

        [Fact]
        public async Task UpdateOperationsController_ShouldReturnOk_WhenStatusQuerySucceeds()
        {
            // Arrange
            var mockQueryHandler = new Mock<IQueryHandler<GetUpdateOperationalStatusQuery, UpdateOperationalStatusDto>>();
            var dto = new UpdateOperationalStatusDto { OrganizationId = _orgId, OverallStatus = "Healthy" };

            mockQueryHandler.Setup(h => h.HandleAsync(It.IsAny<GetUpdateOperationalStatusQuery>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<UpdateOperationalStatusDto>.Success(dto));

            var controller = new UpdateOperationsController(mockQueryHandler.Object);
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            };
            controller.HttpContext.Items["UserPrincipal"] = CreateAdminPrincipal();

            // Act
            var result = await controller.GetOperationalStatusAsync(_orgId, CancellationToken.None);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var returnedDto = Assert.IsType<UpdateOperationalStatusDto>(okResult.Value);
            Assert.Equal("Healthy", returnedDto.OverallStatus);
        }

        [Fact]
        public async Task UpdateDownloadController_ShouldRecordStartedAndCompletedMetrics()
        {
            // Arrange
            var mockDownloadService = new Mock<IUpdateDownloadService>();
            var mockMetrics = new Mock<IUpdateMetrics>();

            var packageId = Guid.NewGuid();
            var memoryStream = new MemoryStream(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

            var prep = UpdateDownloadPreparation.Success(
                packageId,
                Guid.NewGuid(),
                "1.0.0.0",
                "update.spk",
                8,
                UpdateDownloadRangeHelper.ParseRangeHeader(null, 8),
                "checksum",
                "signature",
                "storage_key",
                memoryStream);

            mockDownloadService.Setup(s => s.PrepareDownloadAsync(It.IsAny<UpdateDownloadRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(prep);

            var controller = new UpdateDownloadController(mockDownloadService.Object, mockMetrics.Object);
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            };
            controller.HttpContext.Items["UserPrincipal"] = CreateAdminPrincipal();

            // Act
            var result = await controller.DownloadPackageAsync(packageId, "1.0.0.0", null, null, null, CancellationToken.None);

            // Assert
            Assert.IsType<EmptyResult>(result);
            mockMetrics.Verify(m => m.RecordDownloadStarted("full"), Times.Once);
            mockMetrics.Verify(m => m.RecordDownloadCompleted("full", 8), Times.Once);
        }
    }
}
