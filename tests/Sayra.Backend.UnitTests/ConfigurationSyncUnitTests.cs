using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sayra.Backend.Api.Controllers;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Configuration;
using Sayra.Backend.Application.Configuration.Models;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Shared;
using Xunit;

#nullable enable

namespace Sayra.Backend.UnitTests
{
    public class ConfigurationSyncUnitTests
    {
        private readonly Mock<IRepository<Workstation>> _workstationRepoMock = new();
        private readonly Mock<IConfigurationResolver> _resolverMock = new();
        private readonly Mock<IConfigurationSigningService> _signingServiceMock = new();
        private readonly Mock<IConfigurationHashService> _hashServiceMock = new();
        private readonly Mock<ICanonicalConfigurationSerializer> _canonicalSerializerMock = new();
        private readonly Mock<IConfigurationPackageRepository> _packageRepoMock = new();
        private readonly Mock<IConfigurationDeltaEngine> _deltaEngineMock = new();

        private readonly SynchronizeConfigurationQueryHandler _handler;

        public ConfigurationSyncUnitTests()
        {
            ConfigurationSyncController.ResetRateLimitMap();
            _handler = new SynchronizeConfigurationQueryHandler(
                _workstationRepoMock.Object,
                _resolverMock.Object,
                _signingServiceMock.Object,
                _hashServiceMock.Object,
                _canonicalSerializerMock.Object,
                _packageRepoMock.Object,
                _deltaEngineMock.Object,
                NullLogger<SynchronizeConfigurationQueryHandler>.Instance);
        }

        [Fact]
        public async Task HandleAsync_WorkstationNotFound_ReturnsFailure()
        {
            // Arrange
            _workstationRepoMock.Setup(r => r.GetAllAsync(false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Workstation>());

            var query = new SynchronizeConfigurationQuery("PC-UNKNOWN", 1);

            // Act
            var result = await _handler.HandleAsync(query);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("WORKSTATION_NOT_FOUND", result.ErrorCode);
        }

        [Fact]
        public async Task HandleAsync_DisabledWorkstation_ReturnsFailure()
        {
            // Arrange
            var ws = CreateSampleWorkstation("PC-01", Guid.NewGuid(), Guid.NewGuid());
            ws.IsDisabled = true;

            _workstationRepoMock.Setup(r => r.GetAllAsync(false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Workstation> { ws });

            var query = new SynchronizeConfigurationQuery("PC-01", 1);

            // Act
            var result = await _handler.HandleAsync(query);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("WORKSTATION_DISABLED", result.ErrorCode);
        }

        [Fact]
        public async Task HandleAsync_CrossOrganizationAccess_ReturnsFailure()
        {
            // Arrange
            var orgId1 = Guid.NewGuid();
            var orgId2 = Guid.NewGuid();
            var ws = CreateSampleWorkstation("PC-01", orgId1, Guid.NewGuid());

            _workstationRepoMock.Setup(r => r.GetAllAsync(false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Workstation> { ws });

            var query = new SynchronizeConfigurationQuery("PC-01", 1, OrganizationId: orgId2);

            // Act
            var result = await _handler.HandleAsync(query);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("CROSS_ORGANIZATION_ACCESS_DENIED", result.ErrorCode);
        }

        [Fact]
        public async Task HandleAsync_ClientUpToDate_ReturnsStatusUpToDate()
        {
            // Arrange
            var orgId = Guid.NewGuid();
            var siteId = Guid.NewGuid();
            var ws = CreateSampleWorkstation("PC-01", orgId, siteId);

            _workstationRepoMock.Setup(r => r.GetAllAsync(false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Workstation> { ws });

            var resolutionResult = new ConfigurationResolutionResult
            {
                EffectiveConfigurationJson = "{\"server\":{\"port\":8080}}",
                SchemaVersion = "1.0",
                AppliedSources = new List<AppliedConfigurationSourceDto>
                {
                    new AppliedConfigurationSourceDto { VersionNumber = 44, PackageName = "default", TargetType = "Global" }
                }
            };

            _resolverMock.Setup(r => r.ResolveEffectiveConfigurationAsync(ws.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<ConfigurationResolutionResult>.Success(resolutionResult));

            _signingServiceMock.Setup(s => s.SignPackageAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ConfigurationSignatureResult
                {
                    Hash = "hash-44",
                    Signature = "sig-44",
                    KeyId = "key-01"
                });

            var query = new SynchronizeConfigurationQuery("PC-01", 44, OrganizationId: orgId);

            // Act
            var result = await _handler.HandleAsync(query);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal(ConfigurationSyncStatus.UpToDate, result.Value!.Status);
            Assert.Equal(44, result.Value.VersionNumber);
            Assert.Equal("hash-44", result.Value.Hash);
            Assert.Null(result.Value.Payload);
        }

        [Fact]
        public async Task HandleAsync_ClientMissingVersion_ReturnsFullPackage()
        {
            // Arrange
            var orgId = Guid.NewGuid();
            var ws = CreateSampleWorkstation("PC-01", orgId, Guid.NewGuid());

            _workstationRepoMock.Setup(r => r.GetAllAsync(false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Workstation> { ws });

            var resolutionResult = new ConfigurationResolutionResult
            {
                EffectiveConfigurationJson = "{\"server\":{\"port\":8080}}",
                SchemaVersion = "1.0",
                AppliedSources = new List<AppliedConfigurationSourceDto>
                {
                    new AppliedConfigurationSourceDto { VersionNumber = 44, PackageName = "default", TargetType = "Global" }
                }
            };

            _resolverMock.Setup(r => r.ResolveEffectiveConfigurationAsync(ws.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<ConfigurationResolutionResult>.Success(resolutionResult));

            _signingServiceMock.Setup(s => s.SignPackageAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ConfigurationSignatureResult
                {
                    Hash = "hash-44",
                    Signature = "sig-44",
                    KeyId = "key-01"
                });

            var query = new SynchronizeConfigurationQuery("PC-01", null, OrganizationId: orgId);

            // Act
            var result = await _handler.HandleAsync(query);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal(ConfigurationSyncStatus.FullPackage, result.Value!.Status);
            Assert.Equal(44, result.Value.VersionNumber);
            Assert.Equal("Full", result.Value.PayloadType);
            Assert.NotNull(result.Value.Payload);
        }

        [Fact]
        public async Task HandleAsync_ClientOlder_DeltaAvailable_ReturnsDeltaPackage()
        {
            // Arrange
            var orgId = Guid.NewGuid();
            var ws = CreateSampleWorkstation("PC-01", orgId, Guid.NewGuid());

            _workstationRepoMock.Setup(r => r.GetAllAsync(false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Workstation> { ws });

            string targetEffectiveJson = "{\"server\":{\"port\":9000}}";

            var resolutionResult = new ConfigurationResolutionResult
            {
                EffectiveConfigurationJson = targetEffectiveJson,
                SchemaVersion = "1.0",
                AppliedSources = new List<AppliedConfigurationSourceDto>
                {
                    new AppliedConfigurationSourceDto { VersionNumber = 44, PackageName = "default", TargetType = "Global" }
                }
            };

            _resolverMock.Setup(r => r.ResolveEffectiveConfigurationAsync(ws.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<ConfigurationResolutionResult>.Success(resolutionResult));

            _signingServiceMock.Setup(s => s.SignPackageAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ConfigurationSignatureResult
                {
                    Hash = "hash-44",
                    Signature = "sig-44",
                    KeyId = "key-01"
                });

            var basePkg = ConfigurationPackage.CreateFull("default", 43, "{\"server\":{\"port\":8080}}");
            _packageRepoMock.Setup(p => p.GetByVersionNumberAsync("default", 43, It.IsAny<CancellationToken>()))
                .ReturnsAsync(basePkg);

            var deltas = new List<ConfigurationDelta>
            {
                new ConfigurationDelta { Path = "/server/port", Op = "replace", Value = 9000 }
            };

            _deltaEngineMock.Setup(d => d.ComputeDelta("{\"server\":{\"port\":8080}}", targetEffectiveJson))
                .Returns(deltas);

            _deltaEngineMock.Setup(d => d.ApplyDelta("{\"server\":{\"port\":8080}}", deltas))
                .Returns(targetEffectiveJson);

            _canonicalSerializerMock.Setup(c => c.SerializeToCanonicalBytes(It.IsAny<string>()))
                .Returns((string s) => System.Text.Encoding.UTF8.GetBytes(s));

            _hashServiceMock.Setup(h => h.ComputeHash(It.IsAny<byte[]>()))
                .Returns("hash-44");
            _hashServiceMock.Setup(h => h.VerifyHash(It.IsAny<byte[]>(), "hash-44"))
                .Returns(true);

            var query = new SynchronizeConfigurationQuery("PC-01", 43, OrganizationId: orgId);

            // Act
            var result = await _handler.HandleAsync(query);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal(ConfigurationSyncStatus.DeltaPackage, result.Value!.Status);
            Assert.Equal(44, result.Value.VersionNumber);
            Assert.Equal(43, result.Value.BaseVersionNumber);
            Assert.Equal("Delta", result.Value.PayloadType);
            Assert.Equal(deltas, result.Value.Payload);
        }

        [Fact]
        public async Task HandleAsync_ClientOlder_DeltaUnsafe_FallsBackToFullPackage()
        {
            // Arrange
            var orgId = Guid.NewGuid();
            var ws = CreateSampleWorkstation("PC-01", orgId, Guid.NewGuid());

            _workstationRepoMock.Setup(r => r.GetAllAsync(false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Workstation> { ws });

            string targetEffectiveJson = "{\"server\":{\"port\":9000}}";

            var resolutionResult = new ConfigurationResolutionResult
            {
                EffectiveConfigurationJson = targetEffectiveJson,
                SchemaVersion = "1.0",
                AppliedSources = new List<AppliedConfigurationSourceDto>
                {
                    new AppliedConfigurationSourceDto { VersionNumber = 44, PackageName = "default", TargetType = "Global" }
                }
            };

            _resolverMock.Setup(r => r.ResolveEffectiveConfigurationAsync(ws.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<ConfigurationResolutionResult>.Success(resolutionResult));

            _signingServiceMock.Setup(s => s.SignPackageAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ConfigurationSignatureResult
                {
                    Hash = "hash-44",
                    Signature = "sig-44",
                    KeyId = "key-01"
                });

            // Base version not found
            _packageRepoMock.Setup(p => p.GetByVersionNumberAsync("default", 10, It.IsAny<CancellationToken>()))
                .ReturnsAsync((ConfigurationPackage?)null);

            var query = new SynchronizeConfigurationQuery("PC-01", 10, OrganizationId: orgId);

            // Act
            var result = await _handler.HandleAsync(query);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal(ConfigurationSyncStatus.FullPackage, result.Value!.Status);
            Assert.Equal(44, result.Value.VersionNumber);
            Assert.Equal("Full", result.Value.PayloadType);
        }

        #region Controller Unit Tests
        [Fact]
        public async Task Controller_304NotModified_WhenUpToDate()
        {
            // Arrange
            var syncHandlerMock = new Mock<IQueryHandler<SynchronizeConfigurationQuery, ConfigurationSyncResult>>();
            var controller = new ConfigurationSyncController(syncHandlerMock.Object);

            var httpContext = new DefaultHttpContext();
            var principal = new UserPrincipal
            {
                IsAuthenticated = true,
                PcId = "PC-01",
                OrganizationId = Guid.NewGuid()
            };
            httpContext.Items["UserPrincipal"] = principal;
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

            syncHandlerMock.Setup(h => h.HandleAsync(It.IsAny<SynchronizeConfigurationQuery>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<ConfigurationSyncResult>.Success(new ConfigurationSyncResult
                {
                    Status = ConfigurationSyncStatus.UpToDate,
                    VersionNumber = 44,
                    Version = "v44",
                    Hash = "hash-44"
                }));

            // Act
            var actionResult = await controller.SynchronizeConfigurationAsync("44", CancellationToken.None);

            // Assert
            var statusResult = Assert.IsType<StatusCodeResult>(actionResult);
            Assert.Equal(304, statusResult.StatusCode);
            Assert.Equal("\"hash-44\"", httpContext.Response.Headers["ETag"]);
        }

        [Fact]
        public async Task Controller_200OK_WhenFullPackage()
        {
            // Arrange
            var syncHandlerMock = new Mock<IQueryHandler<SynchronizeConfigurationQuery, ConfigurationSyncResult>>();
            var controller = new ConfigurationSyncController(syncHandlerMock.Object);

            var httpContext = new DefaultHttpContext();
            var principal = new UserPrincipal
            {
                IsAuthenticated = true,
                PcId = "PC-01",
                OrganizationId = Guid.NewGuid()
            };
            httpContext.Items["UserPrincipal"] = principal;
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

            syncHandlerMock.Setup(h => h.HandleAsync(It.IsAny<SynchronizeConfigurationQuery>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<ConfigurationSyncResult>.Success(new ConfigurationSyncResult
                {
                    Status = ConfigurationSyncStatus.FullPackage,
                    VersionNumber = 44,
                    Version = "v44",
                    Hash = "hash-44",
                    Signature = "sig-44",
                    KeyId = "key-01",
                    PayloadType = "Full",
                    Payload = new { port = 8080 },
                    TargetClient = "PC-01"
                }));

            // Act
            var actionResult = await controller.SynchronizeConfigurationAsync(null, CancellationToken.None);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(actionResult);
            var contract = Assert.IsType<ConfigurationPackageContract>(okResult.Value);
            Assert.Equal("v44", contract.Version);
            Assert.Equal("Full", contract.PayloadType);
            Assert.Equal("hash-44", contract.Hash);
            Assert.Equal("sig-44", contract.Signature);
            Assert.Equal("PC-01", contract.TargetClient);
        }

        [Fact]
        public async Task Controller_401Unauthorized_WhenUnauthenticated()
        {
            // Arrange
            var syncHandlerMock = new Mock<IQueryHandler<SynchronizeConfigurationQuery, ConfigurationSyncResult>>();
            var controller = new ConfigurationSyncController(syncHandlerMock.Object);

            var httpContext = new DefaultHttpContext();
            httpContext.Items["UserPrincipal"] = UserPrincipal.Anonymous;
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

            // Act
            var actionResult = await controller.SynchronizeConfigurationAsync("10", CancellationToken.None);

            // Assert
            Assert.IsType<UnauthorizedObjectResult>(actionResult);
        }

        [Fact]
        public async Task Controller_HandlesMalformedVersionStringGracefully()
        {
            // Arrange
            var syncHandlerMock = new Mock<IQueryHandler<SynchronizeConfigurationQuery, ConfigurationSyncResult>>();
            var controller = new ConfigurationSyncController(syncHandlerMock.Object);

            var httpContext = new DefaultHttpContext();
            var principal = new UserPrincipal
            {
                IsAuthenticated = true,
                PcId = "PC-01",
                OrganizationId = Guid.NewGuid()
            };
            httpContext.Items["UserPrincipal"] = principal;
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

            SynchronizeConfigurationQuery? capturedQuery = null;
            syncHandlerMock.Setup(h => h.HandleAsync(It.IsAny<SynchronizeConfigurationQuery>(), It.IsAny<CancellationToken>()))
                .Callback<SynchronizeConfigurationQuery, CancellationToken>((q, _) => capturedQuery = q)
                .ReturnsAsync(Result<ConfigurationSyncResult>.Success(new ConfigurationSyncResult
                {
                    Status = ConfigurationSyncStatus.FullPackage,
                    VersionNumber = 1,
                    Version = "v1",
                    PayloadType = "Full",
                    Payload = new { }
                }));

            // Act
            var actionResult = await controller.SynchronizeConfigurationAsync("vInvalid-1239999999999999999999999999999", CancellationToken.None);

            // Assert
            Assert.IsType<OkObjectResult>(actionResult);
            Assert.NotNull(capturedQuery);
            Assert.Null(capturedQuery!.ClientVersion); // Should gracefully parse as null
        }
        #endregion

        private static Workstation CreateSampleWorkstation(string pcId, Guid orgId, Guid siteId)
        {
            return new Workstation
            {
                PcId = pcId,
                Name = pcId,
                OrganizationEntityId = orgId,
                SiteEntityId = siteId,
                SiteId = "SITE-01",
                Hostname = pcId,
                MacAddress = "00:11:22:33:44:55",
                IpAddress = "192.168.1.100",
                IsDisabled = false,
                IsDeactivated = false,
                IsProvisioned = true
            };
        }
    }
}
