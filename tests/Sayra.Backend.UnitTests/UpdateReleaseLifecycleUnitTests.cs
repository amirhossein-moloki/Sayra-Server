using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Security;
using Sayra.Backend.Application.Updates;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Exceptions;
using Sayra.Backend.Infrastructure.Configuration.Options;
using Sayra.Backend.Infrastructure.Persistence;
using Sayra.Backend.Infrastructure.Security;
using Sayra.Backend.Infrastructure.Updates;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class UpdateReleaseLifecycleUnitTests
    {
        private static ApplicationDbContext CreateInMemoryDbContext(string dbName)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .Options;
            return new ApplicationDbContext(options);
        }

        private static UserPrincipal CreateAdminPrincipal(Guid orgId)
        {
            return new UserPrincipal
            {
                UserId = Guid.NewGuid(),
                Username = "admin_user",
                OrganizationId = orgId,
                Permissions = new List<string> { PermissionCatalog.ManageUpdates, PermissionCatalog.ViewUpdates },
                IsAuthenticated = true
            };
        }

        #region 1. Domain Lifecycle & State Transition Tests

        [Fact]
        public void Release_ValidStateTransitions_SucceedsWithTimestamps()
        {
            var orgId = Guid.NewGuid();
            var release = UpdateRelease.Create(orgId, "v1.0.0", UpdateReleaseType.Standard, "Notes", "admin");

            Assert.Equal(UpdateReleaseStatus.Draft, release.Status);
            Assert.Null(release.PublishedAt);
            Assert.Null(release.SupersededAt);
            Assert.Null(release.RevokedAt);

            release.TransitionTo(UpdateReleaseStatus.Validated);
            Assert.Equal(UpdateReleaseStatus.Validated, release.Status);

            release.TransitionTo(UpdateReleaseStatus.Ready);
            Assert.Equal(UpdateReleaseStatus.Ready, release.Status);

            release.TransitionTo(UpdateReleaseStatus.Published);
            Assert.Equal(UpdateReleaseStatus.Published, release.Status);
            Assert.NotNull(release.PublishedAt);

            release.TransitionTo(UpdateReleaseStatus.Active);
            Assert.Equal(UpdateReleaseStatus.Active, release.Status);

            release.TransitionTo(UpdateReleaseStatus.Superseded);
            Assert.Equal(UpdateReleaseStatus.Superseded, release.Status);
            Assert.NotNull(release.SupersededAt);

            release.TransitionTo(UpdateReleaseStatus.Revoked);
            Assert.Equal(UpdateReleaseStatus.Revoked, release.Status);
            Assert.NotNull(release.RevokedAt);
        }

        [Fact]
        public void Release_InvalidStateTransitions_ThrowsInvalidDomainException()
        {
            var orgId = Guid.NewGuid();
            var release = UpdateRelease.Create(orgId, "v1.0.0");

            // Direct Draft -> Published is invalid
            Assert.Throws<InvalidDomainException>(() => release.TransitionTo(UpdateReleaseStatus.Published));

            // Direct Draft -> Active is invalid
            Assert.Throws<InvalidDomainException>(() => release.TransitionTo(UpdateReleaseStatus.Active));

            // Direct Draft -> Revoked is invalid
            Assert.Throws<InvalidDomainException>(() => release.TransitionTo(UpdateReleaseStatus.Revoked));

            // Terminal state Revoked -> Active is invalid
            release.TransitionTo(UpdateReleaseStatus.Validated);
            release.TransitionTo(UpdateReleaseStatus.Ready);
            release.TransitionTo(UpdateReleaseStatus.Published);
            release.TransitionTo(UpdateReleaseStatus.Revoked);

            Assert.Throws<InvalidDomainException>(() => release.TransitionTo(UpdateReleaseStatus.Active));
            Assert.Throws<InvalidDomainException>(() => release.TransitionTo(UpdateReleaseStatus.Published));
        }

        [Fact]
        public void Release_ImmutabilityEnforcement_BlocksMutationOnPublishedOrActiveOrRevoked()
        {
            var orgId = Guid.NewGuid();
            var release = UpdateRelease.Create(orgId, "v2.0.0");
            release.TransitionTo(UpdateReleaseStatus.Validated);
            release.TransitionTo(UpdateReleaseStatus.Ready);
            release.TransitionTo(UpdateReleaseStatus.Published);

            Assert.True(release.IsImmutableState());

            // Attempting to update metadata on published release throws
            Assert.Throws<InvalidDomainException>(() => release.UpdateMetadata("New notes", "New meta"));

            // Attempting to add package on published release throws
            var pkg = UpdatePackage.Create(release.Id, "app.spk", 1000, "storage/app.spk");
            Assert.Throws<InvalidDomainException>(() => release.AddPackage(pkg));
        }

        #endregion

        #region 2. Application Layer Handler Tests

        [Fact]
        public async Task CreateReleaseHandler_ValidParameters_CreatesReleaseAndRecordsAudit()
        {
            using var dbContext = CreateInMemoryDbContext(Guid.NewGuid().ToString());
            var releaseRepo = new UpdateReleaseRepository(dbContext);
            var authMock = new Mock<IAuthorizationService>();
            var auditMock = new Mock<ISecurityEventService>();

            authMock.Setup(a => a.AuthorizeAsync(It.IsAny<UserPrincipal>(), It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(AuthorizationResult.Allowed());

            var handler = new CreateUpdateReleaseCommandHandler(releaseRepo, dbContext, authMock.Object, auditMock.Object);
            var orgId = Guid.NewGuid();
            var admin = CreateAdminPrincipal(orgId);

            var command = new CreateUpdateReleaseCommand
            {
                OrganizationId = orgId,
                Version = "1.0.0",
                ReleaseType = UpdateReleaseType.Standard,
                ReleaseNotes = "Initial release notes",
                Principal = admin
            };

            var result = await handler.HandleAsync(command);

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal("1.0.0", result.Value.Version);
            Assert.Equal("Draft", result.Value.Status);

            var createdInDb = await releaseRepo.GetByIdAsync(result.Value.ReleaseId);
            Assert.NotNull(createdInDb);
            Assert.Equal(orgId, createdInDb.OrganizationId);

            auditMock.Verify(a => a.RecordSecurityEventAsync(
                "UPDATE_RELEASE_CREATED",
                admin.UserId,
                "User",
                null,
                orgId,
                null,
                "UpdateRelease",
                createdInDb.Id,
                "CREATE_RELEASE",
                "SUCCESS",
                null,
                null,
                null,
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task CreateReleaseHandler_DuplicateVersionInSameOrg_RejectsWithVersionExists()
        {
            using var dbContext = CreateInMemoryDbContext(Guid.NewGuid().ToString());
            var releaseRepo = new UpdateReleaseRepository(dbContext);
            var authMock = new Mock<IAuthorizationService>();
            var auditMock = new Mock<ISecurityEventService>();

            authMock.Setup(a => a.AuthorizeAsync(It.IsAny<UserPrincipal>(), It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(AuthorizationResult.Allowed());

            var orgId = Guid.NewGuid();
            var existingRelease = UpdateRelease.Create(orgId, "1.0.0");
            await releaseRepo.AddAsync(existingRelease);
            await dbContext.SaveChangesAsync();

            var handler = new CreateUpdateReleaseCommandHandler(releaseRepo, dbContext, authMock.Object, auditMock.Object);
            var admin = CreateAdminPrincipal(orgId);

            var command = new CreateUpdateReleaseCommand
            {
                OrganizationId = orgId,
                Version = "1.0.0",
                Principal = admin
            };

            var result = await handler.HandleAsync(command);

            Assert.False(result.IsSuccess);
            Assert.Equal("RELEASE_VERSION_EXISTS", result.ErrorCode);
        }

        [Fact]
        public async Task PrepareReleaseHandler_SignedPackageAndValidArtifact_TransitionsToReady()
        {
            using var dbContext = CreateInMemoryDbContext(Guid.NewGuid().ToString());
            var releaseRepo = new UpdateReleaseRepository(dbContext);
            var packageRepo = new UpdatePackageRepository(dbContext);
            var storageMock = new Mock<IUpdateArtifactStorage>();
            var hashServiceMock = new Mock<IUpdateHashService>();
            var authMock = new Mock<IAuthorizationService>();
            var auditMock = new Mock<ISecurityEventService>();

            var orgId = Guid.NewGuid();
            var release = UpdateRelease.Create(orgId, "1.0.0");
            var package = UpdatePackage.Create(release.Id, "app.spk", 1024, "storage/app.spk");

            package.TransitionLifecycle(UpdatePackageLifecycleState.Uploaded);
            package.TransitionLifecycle(UpdatePackageLifecycleState.Validating);
            package.TransitionLifecycle(UpdatePackageLifecycleState.Validated);
            package.SetIntegrity("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
            package.SignPackage("SampleSigBase64==", "key-1");

            release.AddPackage(package);
            await releaseRepo.AddAsync(release);
            await packageRepo.AddAsync(package);
            await dbContext.SaveChangesAsync();

            var contentStream = new MemoryStream(Encoding.UTF8.GetBytes("package bytes"));
            storageMock.Setup(s => s.OpenReadStreamAsync(package.StorageKey, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(contentStream);

            hashServiceMock.Setup(h => h.ComputeSha256Async(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                           .ReturnsAsync(package.SHA256!);

            authMock.Setup(a => a.AuthorizeAsync(It.IsAny<UserPrincipal>(), It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(AuthorizationResult.Allowed());

            var handler = new PrepareUpdateReleaseCommandHandler(
                releaseRepo, dbContext, storageMock.Object, hashServiceMock.Object, authMock.Object, auditMock.Object);

            var admin = CreateAdminPrincipal(orgId);
            var command = new PrepareUpdateReleaseCommand
            {
                ReleaseId = release.Id,
                Principal = admin
            };

            var result = await handler.HandleAsync(command);

            Assert.True(result.IsSuccess);
            Assert.Equal("Ready", result.Value!.Status);

            var updatedInDb = await releaseRepo.GetByIdAsync(release.Id);
            Assert.NotNull(updatedInDb);
            Assert.Equal(UpdateReleaseStatus.Ready, updatedInDb.Status);
        }

        [Fact]
        public async Task PublishReleaseHandler_UnsignedPackage_FailsPublication()
        {
            using var dbContext = CreateInMemoryDbContext(Guid.NewGuid().ToString());
            var releaseRepo = new UpdateReleaseRepository(dbContext);
            var packageRepo = new UpdatePackageRepository(dbContext);
            var storageMock = new Mock<IUpdateArtifactStorage>();
            var hashServiceMock = new Mock<IUpdateHashService>();
            var authMock = new Mock<IAuthorizationService>();
            var auditMock = new Mock<ISecurityEventService>();

            var orgId = Guid.NewGuid();
            var release = UpdateRelease.Create(orgId, "1.0.0");
            release.TransitionTo(UpdateReleaseStatus.Validated);
            release.TransitionTo(UpdateReleaseStatus.Ready);

            var package = UpdatePackage.Create(release.Id, "app.spk", 1024, "storage/app.spk");
            // Package is in Uploading state (not signed)
            release.AddPackage(package);

            await releaseRepo.AddAsync(release);
            await packageRepo.AddAsync(package);
            await dbContext.SaveChangesAsync();

            authMock.Setup(a => a.AuthorizeAsync(It.IsAny<UserPrincipal>(), It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(AuthorizationResult.Allowed());

            var handler = new PublishUpdateReleaseCommandHandler(
                releaseRepo, dbContext, storageMock.Object, hashServiceMock.Object, authMock.Object, auditMock.Object);

            var admin = CreateAdminPrincipal(orgId);
            var command = new PublishUpdateReleaseCommand
            {
                ReleaseId = release.Id,
                Principal = admin
            };

            var result = await handler.HandleAsync(command);

            Assert.False(result.IsSuccess);
            Assert.Equal("PACKAGE_NOT_SIGNED", result.ErrorCode);
        }

        [Fact]
        public async Task ActivateReleaseHandler_SupersedesExistingActiveRelease()
        {
            using var dbContext = CreateInMemoryDbContext(Guid.NewGuid().ToString());
            var releaseRepo = new UpdateReleaseRepository(dbContext);
            var authMock = new Mock<IAuthorizationService>();
            var auditMock = new Mock<ISecurityEventService>();

            var orgId = Guid.NewGuid();

            // Release 1.0.0 currently active
            var release1 = UpdateRelease.Create(orgId, "1.0.0");
            release1.TransitionTo(UpdateReleaseStatus.Validated);
            release1.TransitionTo(UpdateReleaseStatus.Ready);
            release1.TransitionTo(UpdateReleaseStatus.Published);
            release1.TransitionTo(UpdateReleaseStatus.Active);

            // Release 1.1.0 published and ready for activation
            var release2 = UpdateRelease.Create(orgId, "1.1.0");
            release2.TransitionTo(UpdateReleaseStatus.Validated);
            release2.TransitionTo(UpdateReleaseStatus.Ready);
            release2.TransitionTo(UpdateReleaseStatus.Published);

            await releaseRepo.AddAsync(release1);
            await releaseRepo.AddAsync(release2);
            await dbContext.SaveChangesAsync();

            authMock.Setup(a => a.AuthorizeAsync(It.IsAny<UserPrincipal>(), It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(AuthorizationResult.Allowed());

            var handler = new ActivateUpdateReleaseCommandHandler(releaseRepo, dbContext, authMock.Object, auditMock.Object);
            var admin = CreateAdminPrincipal(orgId);

            var command = new ActivateUpdateReleaseCommand
            {
                ReleaseId = release2.Id,
                Principal = admin
            };

            var result = await handler.HandleAsync(command);

            Assert.True(result.IsSuccess);
            Assert.Equal("Active", result.Value!.Status);

            var dbRelease1 = await releaseRepo.GetByIdAsync(release1.Id);
            var dbRelease2 = await releaseRepo.GetByIdAsync(release2.Id);

            Assert.Equal(UpdateReleaseStatus.Superseded, dbRelease1!.Status);
            Assert.NotNull(dbRelease1.SupersededAt);
            Assert.Equal(UpdateReleaseStatus.Active, dbRelease2!.Status);

            auditMock.Verify(a => a.RecordSecurityEventAsync(
                "UPDATE_RELEASE_SUPERSEDED",
                admin.UserId,
                "User",
                null,
                orgId,
                null,
                "UpdateRelease",
                release1.Id,
                "SUPERSEDE_RELEASE",
                "SUCCESS",
                It.IsAny<string>(),
                null,
                null,
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task RevokeReleaseHandler_RevokesActiveReleaseAndBlocksReactivation()
        {
            using var dbContext = CreateInMemoryDbContext(Guid.NewGuid().ToString());
            var releaseRepo = new UpdateReleaseRepository(dbContext);
            var authMock = new Mock<IAuthorizationService>();
            var auditMock = new Mock<ISecurityEventService>();

            var orgId = Guid.NewGuid();
            var release = UpdateRelease.Create(orgId, "1.0.0");
            release.TransitionTo(UpdateReleaseStatus.Validated);
            release.TransitionTo(UpdateReleaseStatus.Ready);
            release.TransitionTo(UpdateReleaseStatus.Published);
            release.TransitionTo(UpdateReleaseStatus.Active);

            await releaseRepo.AddAsync(release);
            await dbContext.SaveChangesAsync();

            authMock.Setup(a => a.AuthorizeAsync(It.IsAny<UserPrincipal>(), It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(AuthorizationResult.Allowed());

            var revokeHandler = new RevokeUpdateReleaseCommandHandler(releaseRepo, dbContext, authMock.Object, auditMock.Object);
            var admin = CreateAdminPrincipal(orgId);

            var revokeCommand = new RevokeUpdateReleaseCommand
            {
                ReleaseId = release.Id,
                Reason = "Security flaw detected",
                Principal = admin
            };

            var revokeResult = await revokeHandler.HandleAsync(revokeCommand);

            Assert.True(revokeResult.IsSuccess);
            Assert.Equal("Revoked", revokeResult.Value!.Status);

            var dbRelease = await releaseRepo.GetByIdAsync(release.Id);
            Assert.Equal(UpdateReleaseStatus.Revoked, dbRelease!.Status);

            // Attempting to activate revoked release fails
            var activateHandler = new ActivateUpdateReleaseCommandHandler(releaseRepo, dbContext, authMock.Object, auditMock.Object);
            var activateResult = await activateHandler.HandleAsync(new ActivateUpdateReleaseCommand { ReleaseId = release.Id, Principal = admin });

            Assert.False(activateResult.IsSuccess);
            Assert.Equal("INVALID_RELEASE_STATE", activateResult.ErrorCode);
        }

        [Fact]
        public async Task RollbackReleaseHandler_ValidTargetRelease_ReactivatesTargetAndRevokesCurrent()
        {
            using var dbContext = CreateInMemoryDbContext(Guid.NewGuid().ToString());
            var releaseRepo = new UpdateReleaseRepository(dbContext);
            var packageRepo = new UpdatePackageRepository(dbContext);
            var storageMock = new Mock<IUpdateArtifactStorage>();
            var hashServiceMock = new Mock<IUpdateHashService>();
            var authMock = new Mock<IAuthorizationService>();
            var auditMock = new Mock<ISecurityEventService>();

            var orgId = Guid.NewGuid();

            // Historical known-good release 1.0.0 (currently Superseded)
            var release1 = UpdateRelease.Create(orgId, "1.0.0");
            var pkg1 = UpdatePackage.Create(release1.Id, "app-v1.spk", 1024, "storage/app-v1.spk");
            pkg1.TransitionLifecycle(UpdatePackageLifecycleState.Uploaded);
            pkg1.TransitionLifecycle(UpdatePackageLifecycleState.Validating);
            pkg1.TransitionLifecycle(UpdatePackageLifecycleState.Validated);
            pkg1.SetIntegrity("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
            pkg1.SignPackage("Sig1Base64==", "key-1");
            release1.AddPackage(pkg1);

            release1.TransitionTo(UpdateReleaseStatus.Validated);
            release1.TransitionTo(UpdateReleaseStatus.Ready);
            release1.TransitionTo(UpdateReleaseStatus.Published);
            release1.TransitionTo(UpdateReleaseStatus.Active);
            release1.TransitionTo(UpdateReleaseStatus.Superseded);

            // Problematic current release 1.1.0 (currently Active)
            var release2 = UpdateRelease.Create(orgId, "1.1.0");
            var pkg2 = UpdatePackage.Create(release2.Id, "app-v2.spk", 2048, "storage/app-v2.spk");
            pkg2.TransitionLifecycle(UpdatePackageLifecycleState.Uploaded);
            pkg2.TransitionLifecycle(UpdatePackageLifecycleState.Validating);
            pkg2.TransitionLifecycle(UpdatePackageLifecycleState.Validated);
            pkg2.SetIntegrity("a591a6d40bf420404a011733cfb7b190d62c65bf0bcda32b57b277d9ad9f146e");
            pkg2.SignPackage("Sig2Base64==", "key-1");
            release2.AddPackage(pkg2);

            release2.TransitionTo(UpdateReleaseStatus.Validated);
            release2.TransitionTo(UpdateReleaseStatus.Ready);
            release2.TransitionTo(UpdateReleaseStatus.Published);
            release2.TransitionTo(UpdateReleaseStatus.Active);

            await releaseRepo.AddAsync(release1);
            await releaseRepo.AddAsync(release2);
            await packageRepo.AddAsync(pkg1);
            await packageRepo.AddAsync(pkg2);
            await dbContext.SaveChangesAsync();

            // Mock storage artifact verification for target pkg1
            var contentStream = new MemoryStream(Encoding.UTF8.GetBytes("v1 package content"));
            storageMock.Setup(s => s.OpenReadStreamAsync(pkg1.StorageKey, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(contentStream);

            hashServiceMock.Setup(h => h.ComputeSha256Async(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                           .ReturnsAsync(pkg1.SHA256!);

            authMock.Setup(a => a.AuthorizeAsync(It.IsAny<UserPrincipal>(), It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(AuthorizationResult.Allowed());

            var handler = new RollbackUpdateReleaseCommandHandler(
                releaseRepo, dbContext, storageMock.Object, hashServiceMock.Object, authMock.Object, auditMock.Object);

            var admin = CreateAdminPrincipal(orgId);
            var rollbackCommand = new RollbackUpdateReleaseCommand
            {
                CurrentReleaseId = release2.Id,
                TargetReleaseId = release1.Id,
                Reason = "v1.1.0 caused crash on client startup",
                Principal = admin
            };

            var result = await handler.HandleAsync(rollbackCommand);

            Assert.True(result.IsSuccess);
            Assert.Equal("Active", result.Value!.Status);
            Assert.Equal("1.0.0", result.Value.Version);

            var dbRelease1 = await releaseRepo.GetByIdAsync(release1.Id);
            var dbRelease2 = await releaseRepo.GetByIdAsync(release2.Id);

            Assert.Equal(UpdateReleaseStatus.Active, dbRelease1!.Status);
            Assert.Equal(UpdateReleaseStatus.Revoked, dbRelease2!.Status);

            auditMock.Verify(a => a.RecordSecurityEventAsync(
                "UPDATE_RELEASE_ROLLBACK_COMPLETED",
                admin.UserId,
                "User",
                null,
                orgId,
                null,
                "UpdateRelease",
                release1.Id,
                "ROLLBACK_RELEASE",
                "SUCCESS",
                null,
                null,
                null,
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task RollbackReleaseHandler_RevokedTargetRelease_RejectsRollback()
        {
            using var dbContext = CreateInMemoryDbContext(Guid.NewGuid().ToString());
            var releaseRepo = new UpdateReleaseRepository(dbContext);
            var packageRepo = new UpdatePackageRepository(dbContext);
            var storageMock = new Mock<IUpdateArtifactStorage>();
            var hashServiceMock = new Mock<IUpdateHashService>();
            var authMock = new Mock<IAuthorizationService>();
            var auditMock = new Mock<ISecurityEventService>();

            var orgId = Guid.NewGuid();

            // Revoked historical release 1.0.0
            var release1 = UpdateRelease.Create(orgId, "1.0.0");
            release1.TransitionTo(UpdateReleaseStatus.Validated);
            release1.TransitionTo(UpdateReleaseStatus.Ready);
            release1.TransitionTo(UpdateReleaseStatus.Published);
            release1.TransitionTo(UpdateReleaseStatus.Revoked);

            var release2 = UpdateRelease.Create(orgId, "1.1.0");
            release2.TransitionTo(UpdateReleaseStatus.Validated);
            release2.TransitionTo(UpdateReleaseStatus.Ready);
            release2.TransitionTo(UpdateReleaseStatus.Published);
            release2.TransitionTo(UpdateReleaseStatus.Active);

            await releaseRepo.AddAsync(release1);
            await releaseRepo.AddAsync(release2);
            await dbContext.SaveChangesAsync();

            authMock.Setup(a => a.AuthorizeAsync(It.IsAny<UserPrincipal>(), It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(AuthorizationResult.Allowed());

            var handler = new RollbackUpdateReleaseCommandHandler(
                releaseRepo, dbContext, storageMock.Object, hashServiceMock.Object, authMock.Object, auditMock.Object);

            var admin = CreateAdminPrincipal(orgId);
            var rollbackCommand = new RollbackUpdateReleaseCommand
            {
                CurrentReleaseId = release2.Id,
                TargetReleaseId = release1.Id,
                Reason = "Try rollback to 1.0.0",
                Principal = admin
            };

            var result = await handler.HandleAsync(rollbackCommand);

            Assert.False(result.IsSuccess);
            Assert.Equal("TARGET_RELEASE_REVOKED", result.ErrorCode);
        }

        [Fact]
        public async Task Handlers_CrossOrganizationBoundaryAccess_DeniedWithCrossOrgError()
        {
            using var dbContext = CreateInMemoryDbContext(Guid.NewGuid().ToString());
            var releaseRepo = new UpdateReleaseRepository(dbContext);
            var authMock = new Mock<IAuthorizationService>();
            var auditMock = new Mock<ISecurityEventService>();

            var orgA = Guid.NewGuid();
            var orgB = Guid.NewGuid();

            var releaseOrgA = UpdateRelease.Create(orgA, "1.0.0");
            await releaseRepo.AddAsync(releaseOrgA);
            await dbContext.SaveChangesAsync();

            authMock.Setup(a => a.AuthorizeAsync(It.IsAny<UserPrincipal>(), It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(AuthorizationResult.Allowed());

            // Admin belonging to Org B attempts to modify release belonging to Org A
            var adminOrgB = CreateAdminPrincipal(orgB);

            var handler = new UpdateReleaseMetadataCommandHandler(releaseRepo, dbContext, authMock.Object, auditMock.Object);
            var command = new UpdateReleaseMetadataCommand
            {
                ReleaseId = releaseOrgA.Id,
                ReleaseNotes = "Unauthorized notes edit",
                Principal = adminOrgB
            };

            var result = await handler.HandleAsync(command);

            Assert.False(result.IsSuccess);
            Assert.Equal("CROSS_ORGANIZATION_ACCESS_DENIED", result.ErrorCode);
        }

        #endregion
    }
}
