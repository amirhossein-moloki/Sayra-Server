using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Sayra.Backend.Application.Configuration;
using Sayra.Backend.Application.Configuration.Models;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class ConfigurationUnitTests
    {
        private readonly IConfigurationValidator _validator;
        private readonly IConfigurationNormalizer _normalizer;

        public ConfigurationUnitTests()
        {
            _validator = new ConfigurationValidatorService();
            _normalizer = new ConfigurationNormalizer(_validator);
        }

        private static string GetValidJsonPayload()
        {
            return @"{
              ""version"": ""1.0"",
              ""server"": {
                ""ipAddress"": ""192.168.1.100"",
                ""port"": 5000
              },
              ""discovery"": {
                ""enabled"": true,
                ""port"": 37020
              },
              ""heartbeat"": {
                ""intervalSeconds"": 10,
                ""timeoutSeconds"": 30
              },
              ""kiosk"": {
                ""enabled"": true,
                ""allowShellEscape"": false,
                ""autoLoginGamer"": false,
                ""idleTimeoutMinutes"": 15
              },
              ""localization"": {
                ""culture"": ""en-US"",
                ""timeZone"": ""UTC""
              },
              ""security"": {
                ""enableSsl"": true,
                ""requireEncryption"": true,
                ""maxFailedAttempts"": 5
              }
            }";
        }

        [Fact]
        public void ValidConfiguration_Should_PassValidation()
        {
            var validJson = GetValidJsonPayload();
            var result = _validator.Validate(validJson);

            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
        }

        [Fact]
        public void EmptyOrWhitespacePayload_Should_ReturnPayloadEmptyError()
        {
            var result = _validator.Validate("   ");

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Code == "PAYLOAD_EMPTY");
        }

        [Fact]
        public void OversizedPayload_Should_ReturnExceedsMaxSizeError()
        {
            // Create a payload > 100 KB
            var hugeString = new string('a', 105 * 1024);
            var result = _validator.Validate(hugeString);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Code == "EXCEEDS_MAX_SIZE");
        }

        [Fact]
        public void MalformedJson_Should_ReturnInvalidJsonError()
        {
            var invalidJson = @"{ ""version"": ""1.0"", ""server"": { ""port"": 5000 ";
            var result = _validator.Validate(invalidJson);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Code == "INVALID_JSON");
        }

        [Fact]
        public void NonObjectRoot_Should_ReturnNonObjectRootError()
        {
            var jsonArray = @"[""version"", ""1.0""]";
            var result = _validator.Validate(jsonArray);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Code == "NON_OBJECT_ROOT");
        }

        [Fact]
        public void DeeplyNestedJson_Should_ReturnExceedsMaxDepthError()
        {
            // Build JSON nested beyond 16 levels
            var json = @"{""a"":{""b"":{""c"":{""d"":{""e"":{""f"":{""g"":{""h"":{""i"":{""j"":{""k"":{""l"":{""m"":{""n"":{""o"":{""p"":{""q"":{""r"":1}}}}}}}}}}}}}}}}}}";
            var result = _validator.Validate(json);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Code == "EXCEEDS_MAX_DEPTH");
        }

        [Fact]
        public void UnknownProperty_Should_ReturnUnknownPropertyErrorWithPath()
        {
            var jsonWithUnknown = @"{
              ""version"": ""1.0"",
              ""server"": {
                ""ipAddress"": ""127.0.0.1"",
                ""port"": 5000,
                ""unsupportedField"": ""malicious_value""
              },
              ""discovery"": { ""enabled"": true, ""port"": 37020 },
              ""heartbeat"": { ""intervalSeconds"": 10, ""timeoutSeconds"": 30 },
              ""kiosk"": { ""enabled"": true, ""allowShellEscape"": false, ""autoLoginGamer"": false, ""idleTimeoutMinutes"": 15 },
              ""localization"": { ""culture"": ""en-US"", ""timeZone"": ""UTC"" },
              ""security"": { ""enableSsl"": true, ""requireEncryption"": true, ""maxFailedAttempts"": 5 }
            }";

            var result = _validator.Validate(jsonWithUnknown);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Code == "UNKNOWN_PROPERTY" && e.Path == "server.unsupportedField");
        }

        [Fact]
        public void InvalidServerPort_Should_ReturnOutOfRangeError()
        {
            var model = new SayraConfigurationSchema
            {
                Server = new ServerConfigurationSection { IpAddress = "127.0.0.1", Port = 70000 }
            };

            var result = _validator.Validate(model);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Code == "OUT_OF_RANGE" && e.Path == "server.port");
        }

        [Fact]
        public void HeartbeatTimeout_SmallerThanInterval_Should_ReturnInvalidSemanticsError()
        {
            var model = new SayraConfigurationSchema
            {
                Heartbeat = new HeartbeatConfigurationSection
                {
                    IntervalSeconds = 30,
                    TimeoutSeconds = 10 // Invalid: timeout <= interval
                }
            };

            var result = _validator.Validate(model);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Code == "INVALID_SEMANTICS" && e.Path == "heartbeat.timeoutSeconds");
        }

        [Fact]
        public void InvalidCulture_Should_ReturnInvalidCultureError()
        {
            var model = new SayraConfigurationSchema
            {
                Localization = new LocalizationConfigurationSection
                {
                    Culture = "invalid_culture_12345",
                    TimeZone = "UTC"
                }
            };

            var result = _validator.Validate(model);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Code == "INVALID_CULTURE" && e.Path == "localization.culture");
        }

        [Fact]
        public void InvalidTimeZone_Should_ReturnInvalidTimeZoneError()
        {
            var model = new SayraConfigurationSchema
            {
                Localization = new LocalizationConfigurationSection
                {
                    Culture = "en-US",
                    TimeZone = "Invalid/Timezone_6789"
                }
            };

            var result = _validator.Validate(model);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Code == "INVALID_TIMEZONE" && e.Path == "localization.timeZone");
        }

        [Fact]
        public void LogicallyEquivalentJson_WithDifferentPropertyOrder_NormalizesIdentically()
        {
            var json1 = @"{
              ""version"": ""1.0"",
              ""server"": { ""port"": 5000, ""ipAddress"": ""10.0.0.1"" },
              ""discovery"": { ""port"": 37020, ""enabled"": true },
              ""heartbeat"": { ""timeoutSeconds"": 30, ""intervalSeconds"": 10 },
              ""kiosk"": { ""idleTimeoutMinutes"": 15, ""autoLoginGamer"": false, ""allowShellEscape"": false, ""enabled"": true },
              ""localization"": { ""timeZone"": ""UTC"", ""culture"": ""en-US"" },
              ""security"": { ""maxFailedAttempts"": 5, ""requireEncryption"": true, ""enableSsl"": true }
            }";

            var json2 = @"{
              ""security"": { ""enableSsl"": true, ""maxFailedAttempts"": 5, ""requireEncryption"": true },
              ""localization"": { ""culture"": ""en-US"", ""timeZone"": ""UTC"" },
              ""kiosk"": { ""enabled"": true, ""allowShellEscape"": false, ""autoLoginGamer"": false, ""idleTimeoutMinutes"": 15 },
              ""heartbeat"": { ""intervalSeconds"": 10, ""timeoutSeconds"": 30 },
              ""discovery"": { ""enabled"": true, ""port"": 37020 },
              ""server"": { ""ipAddress"": ""10.0.0.1"", ""port"": 5000 },
              ""version"": ""1.0""
            }";

            var normalized1 = _normalizer.NormalizeToJson(json1);
            var normalized2 = _normalizer.NormalizeToJson(json2);

            Assert.Equal(normalized1, normalized2);
        }

        [Fact]
        public void CultureString_NormalizesToStandardFormat()
        {
            var json = @"{
              ""version"": ""1.0"",
              ""server"": { ""ipAddress"": ""10.0.0.1"", ""port"": 5000 },
              ""discovery"": { ""enabled"": true, ""port"": 37020 },
              ""heartbeat"": { ""intervalSeconds"": 10, ""timeoutSeconds"": 30 },
              ""kiosk"": { ""enabled"": true, ""allowShellEscape"": false, ""autoLoginGamer"": false, ""idleTimeoutMinutes"": 15 },
              ""localization"": { ""culture"": ""en-us"", ""timeZone"": ""UTC"" },
              ""security"": { ""enableSsl"": true, ""requireEncryption"": true, ""maxFailedAttempts"": 5 }
            }";

            var normalized = _normalizer.NormalizeToJson(json);

            Assert.Contains(@"""culture"":""en-US""", normalized);
        }

        [Fact]
        public void Normalization_IsDeterministic_AcrossRepeatedExecutions()
        {
            var json = GetValidJsonPayload();

            var firstRun = _normalizer.NormalizeToJson(json);

            for (int i = 0; i < 100; i++)
            {
                var run = _normalizer.NormalizeToJson(json);
                Assert.Equal(firstRun, run);
            }
        }

        [Fact]
        public void NormalizingInvalidConfiguration_ThrowsInvalidOperationException()
        {
            var invalidJson = @"{
              ""version"": ""1.0"",
              ""server"": { ""ipAddress"": ""10.0.0.1"", ""port"": -1 }
            }";

            Assert.Throws<InvalidOperationException>(() => _normalizer.NormalizeToJson(invalidJson));
        }

        [Fact]
        public async Task ValidateConfigurationCommandHandler_ReturnsValidationResult()
        {
            var handler = new ValidateConfigurationCommandHandler(_validator);
            var command = new ValidateConfigurationCommand(RawPayload: GetValidJsonPayload());

            var result = await handler.HandleAsync(command, default);

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.True(result.Value.IsValid);
        }

        [Fact]
        public async Task NormalizeConfigurationCommandHandler_ReturnsNormalizedJson()
        {
            var handler = new NormalizeConfigurationCommandHandler(_normalizer);
            var command = new NormalizeConfigurationCommand(RawPayload: GetValidJsonPayload());

            var result = await handler.HandleAsync(command, default);

            Assert.True(result.IsSuccess);
            Assert.False(string.IsNullOrWhiteSpace(result.Value));
            Assert.Contains(@"""version"":""1.0""", result.Value);
        }

        [Fact]
        public void ValidationErrors_DoNotLeakSecretsOrSensitiveInformation()
        {
            var jsonWithSecretKey = @"{
              ""version"": ""1.0"",
              ""server"": {
                ""ipAddress"": ""127.0.0.1"",
                ""port"": 5000,
                ""secret_key_12345"": ""SuperSecretTokenString""
              },
              ""discovery"": { ""enabled"": true, ""port"": 37020 },
              ""heartbeat"": { ""intervalSeconds"": 10, ""timeoutSeconds"": 30 },
              ""kiosk"": { ""enabled"": true, ""allowShellEscape"": false, ""autoLoginGamer"": false, ""idleTimeoutMinutes"": 15 },
              ""localization"": { ""culture"": ""en-US"", ""timeZone"": ""UTC"" },
              ""security"": { ""enableSsl"": true, ""requireEncryption"": true, ""maxFailedAttempts"": 5 }
            }";

            var result = _validator.Validate(jsonWithSecretKey);

            Assert.False(result.IsValid);
            foreach (var err in result.Errors)
            {
                Assert.DoesNotContain("SuperSecretTokenString", err.Message);
            }
        }
    }
}
