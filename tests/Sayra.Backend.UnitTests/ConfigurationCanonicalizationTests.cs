using System;
using Sayra.Backend.Application.Configuration;
using Sayra.Backend.Application.Configuration.Models;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class ConfigurationCanonicalizationTests
    {
        private readonly ICanonicalConfigurationSerializer _serializer = new CanonicalConfigurationSerializer();

        [Fact]
        public void PropertyOrdering_ScrambledObjectKeys_NormalizedToOrdinalSortedOrder()
        {
            string payload1 = @"{""z"":1,""a"":2,""m"":3}";
            string payload2 = @"{""a"":2,""m"":3,""z"":1}";

            string canonical1 = _serializer.SerializeToCanonicalJson(payload1);
            string canonical2 = _serializer.SerializeToCanonicalJson(payload2);

            Assert.Equal(canonical1, canonical2);
            Assert.Equal(@"{""a"":2,""m"":3,""z"":1}", canonical1);
        }

        [Fact]
        public void ArrayHandling_PreservesExactElementOrder()
        {
            string arrayOrder1 = @"[""a"",""b"",""c""]";
            string arrayOrder2 = @"[""c"",""b"",""a""]";

            string canonical1 = _serializer.SerializeToCanonicalJson(arrayOrder1);
            string canonical2 = _serializer.SerializeToCanonicalJson(arrayOrder2);

            Assert.NotEqual(canonical1, canonical2);
            Assert.Equal(@"[""a"",""b"",""c""]", canonical1);
            Assert.Equal(@"[""c"",""b"",""a""]", canonical2);
        }

        [Fact]
        public void Formatting_BooleansNumbersNulls_FormattedDeterministically()
        {
            string raw = @"{""bTrue"": true, ""bFalse"": false, ""numInt"": 10, ""numDec"": 12.34, ""nullVal"": null}";

            string canonical = _serializer.SerializeToCanonicalJson(raw);

            Assert.Contains(@"""bFalse"":false", canonical);
            Assert.Contains(@"""bTrue"":true", canonical);
            Assert.Contains(@"""nullVal"":null", canonical);
            Assert.Contains(@"""numDec"":12.34", canonical);
            Assert.Contains(@"""numInt"":10", canonical);
        }

        [Fact]
        public void SchemaModel_SerializedToIdenticalCanonicalRepresentation()
        {
            var schema = new SayraConfigurationSchema
            {
                Version = "1.0",
                Server = new ServerConfigurationSection { IpAddress = "10.0.0.1", Port = 8080 },
                Discovery = new DiscoveryConfigurationSection { Enabled = true, Port = 37020 },
                Heartbeat = new HeartbeatConfigurationSection { IntervalSeconds = 15, TimeoutSeconds = 45 },
                Kiosk = new KioskConfigurationSection { Enabled = true, AllowShellEscape = false, AutoLoginGamer = false, IdleTimeoutMinutes = 10 },
                Localization = new LocalizationConfigurationSection { Culture = "en-US", TimeZone = "UTC" },
                Security = new SecurityConfigurationSection { EnableSsl = true, RequireEncryption = true, MaxFailedAttempts = 3 }
            };

            string canonicalFromModel = _serializer.SerializeToCanonicalJson(schema);

            string rawJson = @"{
              ""server"": { ""ipAddress"": ""10.0.0.1"", ""port"": 8080 },
              ""version"": ""1.0"",
              ""security"": { ""enableSsl"": true, ""requireEncryption"": true, ""maxFailedAttempts"": 3 },
              ""localization"": { ""culture"": ""en-US"", ""timeZone"": ""UTC"" },
              ""kiosk"": { ""enabled"": true, ""allowShellEscape"": false, ""autoLoginGamer"": false, ""idleTimeoutMinutes"": 10 },
              ""heartbeat"": { ""intervalSeconds"": 15, ""timeoutSeconds"": 45 },
              ""discovery"": { ""enabled"": true, ""port"": 37020 }
            }";

            string canonicalFromRaw = _serializer.SerializeToCanonicalJson(rawJson);

            Assert.Equal(canonicalFromRaw, canonicalFromModel);
        }
    }
}
