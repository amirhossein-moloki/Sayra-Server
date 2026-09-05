using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sayra.Backend.Application.Updates;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Exceptions;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class UpdateClientContractTests
    {
        #region 1. Serialization & Golden Contract Tests

        [Fact]
        public void ManifestContract_Serialization_ProducesExpectedCamelCaseJson()
        {
            var manifest = new ClientUpdateManifestContract
            {
                Version = "1.2.3",
                ReleaseNotes = "Bug fixes and performance improvements",
                PackageUrl = "https://updates.sayra.internal/api/updates/download/abc-123",
                Checksum = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                Signature = "MEYCIQ...==",
                IsMandatory = true,
                MinimumSupportedVersion = "1.0.0",
                FileSize = 1048576,
                PackageType = "spk"
            };

            string json = ProtocolSerialization.Serialize(manifest);

            Assert.Contains("\"version\":\"1.2.3\"", json);
            Assert.Contains("\"releaseNotes\":\"Bug fixes and performance improvements\"", json);
            Assert.Contains("\"packageUrl\":", json);
            Assert.Contains("\"checksum\":\"e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855\"", json);
            Assert.Contains("\"signature\":\"MEYCIQ...==\"", json);
            Assert.Contains("\"isMandatory\":true", json);
            Assert.Contains("\"fileSize\":1048576", json);
            Assert.Contains("\"packageType\":\"spk\"", json);
        }

        [Fact]
        public void ManifestContract_Deserialization_HandlesCamelCaseAndCaseInsensitiveJson()
        {
            string json = @"{
                ""version"": ""2.1.0"",
                ""releaseNotes"": ""Security update"",
                ""packageUrl"": ""http://localhost/api/updates/download/pkg-1"",
                ""checksum"": ""a591a6d40bf420404a011733cfb7b190d62c65bf0bcda32b57b277d9ad9f146e"",
                ""signature"": ""Base64SigVal"",
                ""isMandatory"": true,
                ""fileSize"": 2097152,
                ""packageType"": ""spk""
            }";

            var deserialized = ProtocolSerialization.Deserialize<ClientUpdateManifestContract>(json);

            Assert.NotNull(deserialized);
            Assert.Equal("2.1.0", deserialized.Version);
            Assert.Equal("Security update", deserialized.ReleaseNotes);
            Assert.Equal("http://localhost/api/updates/download/pkg-1", deserialized.PackageUrl);
            Assert.Equal("a591a6d40bf420404a011733cfb7b190d62c65bf0bcda32b57b277d9ad9f146e", deserialized.Checksum);
            Assert.Equal("Base64SigVal", deserialized.Signature);
            Assert.True(deserialized.IsMandatory);
            Assert.Equal(2097152, deserialized.FileSize);
            Assert.Equal("spk", deserialized.PackageType);
        }

        #endregion

        #region 2. Version Comparison Semantics Tests

        [Theory]
        [InlineData("1.0.0", "1.0.1", true)]
        [InlineData("1.0.0.0", "1.0.0.1", true)]
        [InlineData("2.0.0", "1.9.9", false)]
        [InlineData("1.0.0", "1.0.0", false)]
        [InlineData("0.9.1", "1.0.0", true)]
        public void ClientVersionComparer_IsUpdateAvailable_EvaluatesCorrectly(string current, string target, bool expectedAvailable)
        {
            bool isAvailable = ClientVersionComparer.IsUpdateAvailable(current, target);
            Assert.Equal(expectedAvailable, isAvailable);
        }

        [Theory]
        [InlineData("1.0.0", "1.1.0", false)]
        [InlineData("1.0.0", "1.0.0", false)]
        [InlineData("1.0.0", "0.9.0", true)]
        public void ClientVersionComparer_IsDowngrade_DetectsDowngrades(string current, string target, bool expectedDowngrade)
        {
            bool isDowngrade = ClientVersionComparer.IsDowngrade(current, target);
            Assert.Equal(expectedDowngrade, isDowngrade);
        }

        [Theory]
        [InlineData("0.9.0", "1.0.0", true)]
        [InlineData("1.0.0", "1.0.0", false)]
        [InlineData("1.1.0", "1.0.0", false)]
        [InlineData("1.0.0", null, false)]
        [InlineData("1.0.0", "", false)]
        public void ClientVersionComparer_IsBelowMinimumVersion_EvaluatesMinimumThreshold(string current, string? minimum, bool expectedBelow)
        {
            bool isBelow = ClientVersionComparer.IsBelowMinimumVersion(current, minimum);
            Assert.Equal(expectedBelow, isBelow);
        }

        [Fact]
        public void ClientVersionComparer_SemVerPrerelease_EvaluatesPrereleaseLowerThanRelease()
        {
            // 1.0.0-beta.1 is lower than 1.0.0
            int comp = ClientVersionComparer.Compare("1.0.0-beta.1", "1.0.0");
            Assert.True(comp < 0);

            // 1.0.0 is greater than 1.0.0-rc.1
            Assert.True(ClientVersionComparer.IsUpdateAvailable("1.0.0-rc.1", "1.0.0"));
        }

        [Fact]
        public void ClientVersionComparer_InvalidVersion_ThrowsInvalidDomainException()
        {
            Assert.Throws<InvalidDomainException>(() => ClientVersionComparer.Compare("invalid-version", "1.0.0"));
            Assert.Throws<InvalidDomainException>(() => ClientVersionComparer.Compare("1.99999999999999999999.0", "1.0.0"));
        }

        #endregion

        #region 3. Adapter Mapping Tests

        [Fact]
        public void Adapter_ToManifestContract_MapsReleaseAndPackageCorrectly()
        {
            var orgId = Guid.NewGuid();
            var release = UpdateRelease.Create(orgId, "1.5.0", UpdateReleaseType.Security, "Critical security patch");
            var package = UpdatePackage.Create(release.Id, "patch.spk", 5242880, "updates/patch.spk", UpdatePackageType.Spk);

            package.SetIntegrityAndSignature("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", "DummySig", "key-1");
            release.AddPackage(package);

            var contract = ClientUpdateContractAdapter.ToManifestContract(release, package, "https://api.sayra.io");

            Assert.Equal("1.5.0", contract.Version);
            Assert.Equal("Critical security patch", contract.ReleaseNotes);
            Assert.Equal($"https://api.sayra.io/api/updates/download/{package.Id}", contract.PackageUrl);
            Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", contract.Checksum);
            Assert.Equal("DummySig", contract.Signature);
            Assert.True(contract.IsMandatory);
            Assert.Equal(5242880, contract.FileSize);
            Assert.Equal("spk", contract.PackageType);
        }

        [Fact]
        public void Adapter_ToPackageMetadataContract_MapsPropertiesAccurately()
        {
            var releaseId = Guid.NewGuid();
            var package = UpdatePackage.Create(releaseId, "app-v2.spk", 1048576, "storage/v2.spk", UpdatePackageType.Spk);
            package.SetIntegrityAndSignature("a591a6d40bf420404a011733cfb7b190d62c65bf0bcda32b57b277d9ad9f146e", "SigValue", "key-2");

            var metadataContract = ClientUpdateContractAdapter.ToPackageMetadataContract(package);

            Assert.Equal(package.Id, metadataContract.PackageId);
            Assert.Equal(releaseId, metadataContract.ReleaseId);
            Assert.Equal("app-v2.spk", metadataContract.FileName);
            Assert.Equal(1048576, metadataContract.Size);
            Assert.Equal("a591a6d40bf420404a011733cfb7b190d62c65bf0bcda32b57b277d9ad9f146e", metadataContract.ChecksumSha256);
            Assert.Equal("SigValue", metadataContract.Signature);
            Assert.Equal("key-2", metadataContract.SigningKeyId);
            Assert.Equal("spk", metadataContract.PackageType);
            Assert.Equal("storage/v2.spk", metadataContract.StorageKey);
        }

        [Fact]
        public void Adapter_ToLegacyManifest_ConvertsContractToLegacyObject()
        {
            var contract = new ClientUpdateManifestContract
            {
                Version = "1.0.0",
                ReleaseNotes = "Legacy release",
                PackageUrl = "https://server/api/updates/download/1",
                Checksum = "1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef",
                Signature = "LegacySig"
            };

            var legacy = ClientUpdateContractAdapter.ToLegacyManifest(contract);

            Assert.Equal(contract.Version, legacy.Version);
            Assert.Equal(contract.ReleaseNotes, legacy.ReleaseNotes);
            Assert.Equal(contract.PackageUrl, legacy.PackageUrl);
            Assert.Equal(contract.Checksum, legacy.Checksum);
            Assert.Equal(contract.Signature, legacy.Signature);
        }

        #endregion

        #region 4. Signature Cryptographic Fixture Test

        [Fact]
        public void RSA_SHA256_Signature_VerificationFixture_ValidatesSignatureCorrectly()
        {
            using var rsa = RSA.Create(2048);
            byte[] payloadBytes = Encoding.UTF8.GetBytes("sayra-package-content-payload");

            byte[] signatureBytes = rsa.SignData(payloadBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            string base64Signature = Convert.ToBase64String(signatureBytes);

            // Verify with public key
            byte[] verifyBytes = Convert.FromBase64String(base64Signature);
            bool isValid = rsa.VerifyData(payloadBytes, verifyBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            Assert.True(isValid);

            // Tampered payload verification fails
            byte[] tamperedPayload = Encoding.UTF8.GetBytes("tampered-sayra-package-payload");
            bool isTamperedValid = rsa.VerifyData(tamperedPayload, verifyBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            Assert.False(isTamperedValid);
        }

        #endregion
    }
}
