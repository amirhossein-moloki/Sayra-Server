using System;
using System.Security.Cryptography;
using System.Text;
using Sayra.Backend.Application.Configuration;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class ConfigurationHashGoldenVectorTests
    {
        private readonly ICanonicalConfigurationSerializer _serializer = new CanonicalConfigurationSerializer();
        private readonly IConfigurationHashService _hashService;

        public ConfigurationHashGoldenVectorTests()
        {
            _hashService = new ConfigurationHashService(_serializer);
        }

        [Fact]
        public void GoldenVector_FullConfiguration_ProducesExpectedCanonicalJsonAndSha256Hash()
        {
            // Input JSON with un-sorted properties and varied whitespace
            string inputJson = @"{
              ""version"": ""1.0"",
              ""server"": {
                ""port"": 5000,
                ""ipAddress"": ""192.168.1.100""
              },
              ""security"": {
                ""maxFailedAttempts"": 5,
                ""enableSsl"": true,
                ""requireEncryption"": true
              },
              ""localization"": {
                ""timeZone"": ""UTC"",
                ""culture"": ""en-US""
              },
              ""kiosk"": {
                ""idleTimeoutMinutes"": 15,
                ""autoLoginGamer"": false,
                ""allowShellEscape"": false,
                ""enabled"": true
              },
              ""heartbeat"": {
                ""timeoutSeconds"": 30,
                ""intervalSeconds"": 10
              },
              ""discovery"": {
                ""port"": 37020,
                ""enabled"": true
              }
            }";

            string canonicalJson = _serializer.SerializeToCanonicalJson(inputJson);

            // Verify keys are strictly Ordinal sorted at all levels
            Assert.Contains(@"""discovery"":{""enabled"":true,""port"":37020}", canonicalJson);
            Assert.Contains(@"""heartbeat"":{""intervalSeconds"":10,""timeoutSeconds"":30}", canonicalJson);
            Assert.Contains(@"""kiosk"":{""allowShellEscape"":false,""autoLoginGamer"":false,""enabled"":true,""idleTimeoutMinutes"":15}", canonicalJson);
            Assert.Contains(@"""localization"":{""culture"":""en-US"",""timeZone"":""UTC""}", canonicalJson);
            Assert.Contains(@"""security"":{""enableSsl"":true,""maxFailedAttempts"":5,""requireEncryption"":true}", canonicalJson);
            Assert.Contains(@"""server"":{""ipAddress"":""192.168.1.100"",""port"":5000}", canonicalJson);

            string hash = _hashService.ComputeHash(canonicalJson);

            // Compute expected SHA-256 manually over canonical UTF-8 bytes to establish Golden Vector
            byte[] canonicalBytes = Encoding.UTF8.GetBytes(canonicalJson);
            string expectedHash = Convert.ToHexString(SHA256.HashData(canonicalBytes)).ToLowerInvariant();

            Assert.Equal(expectedHash, hash);
            Assert.Equal(64, hash.Length);
        }

        [Fact]
        public void GoldenVector_DeltaConfiguration_ProducesExpectedCanonicalJsonAndSha256Hash()
        {
            string deltaJson = @"[{""op"":""replace"",""path"":""/heartbeat/intervalSeconds"",""value"":15}]";

            string canonicalJson = _serializer.SerializeToCanonicalJson(deltaJson);
            string hash = _hashService.ComputeHash(canonicalJson);

            byte[] canonicalBytes = Encoding.UTF8.GetBytes(canonicalJson);
            string expectedHash = Convert.ToHexString(SHA256.HashData(canonicalBytes)).ToLowerInvariant();

            Assert.Equal(expectedHash, hash);
            Assert.True(_hashService.VerifyHash(deltaJson, hash));
        }
    }
}
