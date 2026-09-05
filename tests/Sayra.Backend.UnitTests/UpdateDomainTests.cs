using System;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Exceptions;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class UpdateDomainTests
    {
        [Fact]
        public void CreateRelease_WithValidParameters_ReturnsDraftRelease()
        {
            var orgId = Guid.NewGuid();
            var release = UpdateRelease.Create(orgId, "1.2.0-hotfix", UpdateReleaseType.Hotfix, "Bug fixes", "admin-1");

            Assert.NotEqual(Guid.Empty, release.Id);
            Assert.Equal(orgId, release.OrganizationId);
            Assert.Equal("1.2.0-hotfix", release.Version);
            Assert.Equal(UpdateReleaseType.Hotfix, release.ReleaseType);
            Assert.Equal(UpdateReleaseStatus.Draft, release.Status);
            Assert.Equal("Bug fixes", release.ReleaseNotes);
            Assert.Equal("admin-1", release.CreatedBy);
            Assert.False(release.IsImmutableState());
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("1.0/invalid")]
        [InlineData("1.0?bad")]
        public void CreateRelease_WithInvalidVersion_ThrowsInvalidDomainException(string? invalidVersion)
        {
            var orgId = Guid.NewGuid();
            var ex = Assert.Throws<InvalidDomainException>(() => UpdateRelease.Create(orgId, invalidVersion!));
            Assert.True(ex.ErrorCode == "INVALID_VERSION" || ex.ErrorCode == "INVALID_VERSION_FORMAT");
        }

        [Fact]
        public void ReleaseTransition_ValidSequence_UpdatesStatusAndTimestamps()
        {
            var orgId = Guid.NewGuid();
            var release = UpdateRelease.Create(orgId, "v2.0.0");

            release.TransitionTo(UpdateReleaseStatus.Validated);
            Assert.Equal(UpdateReleaseStatus.Validated, release.Status);

            release.TransitionTo(UpdateReleaseStatus.Ready);
            Assert.Equal(UpdateReleaseStatus.Ready, release.Status);

            release.TransitionTo(UpdateReleaseStatus.Published);
            Assert.Equal(UpdateReleaseStatus.Published, release.Status);
            Assert.NotNull(release.PublishedAt);
            Assert.True(release.IsImmutableState());

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
        public void ReleaseTransition_IllegalTransition_ThrowsInvalidDomainException()
        {
            var release = UpdateRelease.Create(Guid.NewGuid(), "1.0.0");

            // Cannot jump from Draft directly to Active
            var ex = Assert.Throws<InvalidDomainException>(() => release.TransitionTo(UpdateReleaseStatus.Active));
            Assert.Equal("INVALID_RELEASE_TRANSITION", ex.ErrorCode);
        }

        [Fact]
        public void Release_ModifyWhenPublished_ThrowsInvalidDomainException()
        {
            var release = UpdateRelease.Create(Guid.NewGuid(), "1.0.0");
            release.TransitionTo(UpdateReleaseStatus.Validated);
            release.TransitionTo(UpdateReleaseStatus.Ready);
            release.TransitionTo(UpdateReleaseStatus.Published);

            var ex = Assert.Throws<InvalidDomainException>(() => release.UpdateMetadata("New notes", null));
            Assert.Equal("RELEASE_IMMUTABLE", ex.ErrorCode);
        }

        [Fact]
        public void CreatePackage_WithValidParameters_ReturnsUploadingPackage()
        {
            var releaseId = Guid.NewGuid();
            var package = UpdatePackage.Create(releaseId, "update-v1.spk", 1024576, "updates/2026/update-v1.spk", UpdatePackageType.Spk, "local");

            Assert.NotEqual(Guid.Empty, package.Id);
            Assert.Equal(releaseId, package.ReleaseId);
            Assert.Equal("update-v1.spk", package.FileName);
            Assert.Equal(1024576, package.Size);
            Assert.Equal("updates/2026/update-v1.spk", package.StorageKey);
            Assert.Equal(UpdatePackageLifecycleState.Uploading, package.LifecycleState);
            Assert.Equal(UpdatePackageVerificationStatus.NotVerified, package.VerificationStatus);
        }

        [Theory]
        [InlineData("../unsafe.spk")]
        [InlineData("path/with/../traversal.spk")]
        [InlineData("")]
        [InlineData("   ")]
        public void CreatePackage_WithUnsafeStorageKey_ThrowsInvalidDomainException(string storageKey)
        {
            var releaseId = Guid.NewGuid();
            var ex = Assert.Throws<InvalidDomainException>(() => UpdatePackage.Create(releaseId, "package.spk", 100, storageKey));
            Assert.True(ex.ErrorCode == "INVALID_STORAGE_KEY" || ex.ErrorCode == "UNSAFE_STORAGE_KEY");
        }

        [Fact]
        public void Package_SetIntegrityAndSignature_ValidatesHashFormatAndState()
        {
            var releaseId = Guid.NewGuid();
            var package = UpdatePackage.Create(releaseId, "package.spk", 100, "key1");

            var validSha256 = new string('a', 64);
            package.SetIntegrityAndSignature(validSha256, "SIG_123", "KEY_456");

            Assert.Equal(validSha256, package.SHA256);
            Assert.Equal("SIG_123", package.Signature);
            Assert.Equal("KEY_456", package.SigningKeyId);

            var invalidShaEx = Assert.Throws<InvalidDomainException>(() => package.SetIntegrityAndSignature("invalid_hash", "SIG", "KEY"));
            Assert.Equal("INVALID_HASH_FORMAT", invalidShaEx.ErrorCode);
        }

        [Fact]
        public void Package_LifecycleTransition_ValidatesTransitions()
        {
            var package = UpdatePackage.Create(Guid.NewGuid(), "pkg.spk", 500, "storage_key_1");

            package.TransitionLifecycle(UpdatePackageLifecycleState.Uploaded);
            package.TransitionLifecycle(UpdatePackageLifecycleState.Validating);
            Assert.Equal(UpdatePackageVerificationStatus.Validating, package.VerificationStatus);

            package.TransitionLifecycle(UpdatePackageLifecycleState.Validated);
            Assert.Equal(UpdatePackageVerificationStatus.Valid, package.VerificationStatus);

            package.TransitionLifecycle(UpdatePackageLifecycleState.Signed);
            package.TransitionLifecycle(UpdatePackageLifecycleState.Ready);
            Assert.True(package.IsImmutableArtifactState());

            // Illegal transition back to Uploading
            var ex = Assert.Throws<InvalidDomainException>(() => package.TransitionLifecycle(UpdatePackageLifecycleState.Uploading));
            Assert.Equal("INVALID_PACKAGE_LIFECYCLE_TRANSITION", ex.ErrorCode);
        }
    }
}
