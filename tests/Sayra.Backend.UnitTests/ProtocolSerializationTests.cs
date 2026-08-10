using System;
using System.Collections.Generic;
using System.Text.Json;
using Xunit;
using Sayra.Backend.Contracts;

namespace Sayra.Backend.UnitTests
{
    public class ProtocolSerializationTests
    {
        #region 1. JSON Serialization Test
        [Fact]
        public void Test_01_JsonSerialization_ShouldProduceValidJson()
        {
            var msg = new HeartbeatMessage { PcId = "PC-01", Timestamp = new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc) };
            string json = ProtocolSerialization.Serialize(msg);

            Assert.NotNull(json);
            Assert.Contains("\"pcId\":\"PC-01\"", json);
            Assert.Contains("\"type\":\"HEARTBEAT\"", json);
        }
        #endregion

        #region 2. JSON Deserialization Test
        [Fact]
        public void Test_02_JsonDeserialization_ShouldReconstructObjects()
        {
            string json = "{\"type\":\"HEARTBEAT\",\"pcId\":\"PC-02\",\"timestamp\":\"2026-08-10T12:00:00Z\"}";
            var msg = ProtocolSerialization.Deserialize<HeartbeatMessage>(json);

            Assert.NotNull(msg);
            Assert.Equal("HEARTBEAT", msg.Type);
            Assert.Equal("PC-02", msg.PcId);
            Assert.Equal(new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc), msg.Timestamp.ToUniversalTime());
        }
        #endregion

        #region 3. Exact Property Name Test
        [Fact]
        public void Test_03_ExactPropertyNames_ShouldMatchClientExpectations()
        {
            var response = new DiscoveryResponse
            {
                Type = "SAYRA_SERVER_RESPONSE",
                ServerId = "srv-123",
                ServerName = "CentralSayra",
                Ip = "192.168.1.100",
                TcpPort = 5000,
                ApiPort = 8080,
                Version = "1.0.0",
                Timestamp = new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc),
                Nonce = "abcde",
                Signature = "signed-data"
            };

            string json = ProtocolSerialization.Serialize(response);

            // Exact property names checks
            Assert.Contains("\"type\":", json);
            Assert.Contains("\"serverId\":", json);
            Assert.Contains("\"serverName\":", json);
            Assert.Contains("\"ip\":", json);
            Assert.Contains("\"tcpPort\":", json);
            Assert.Contains("\"apiPort\":", json);
            Assert.Contains("\"version\":", json);
            Assert.Contains("\"timestamp\":", json);
            Assert.Contains("\"nonce\":", json);
            Assert.Contains("\"signature\":", json);
        }
        #endregion

        #region 4. Enum Serialization Test
        [Fact]
        public void Test_04_EnumSerialization_ShouldUseStringRepresentation()
        {
            var status = AuthenticationStatus.SUCCESS;
            string json = ProtocolSerialization.Serialize(status);

            // Enums must serialize to string representation (camelCase or exact match)
            Assert.Equal("\"success\"", json);

            var deserialized = ProtocolSerialization.Deserialize<AuthenticationStatus>("\"failed\"");
            Assert.Equal(AuthenticationStatus.FAILED, deserialized);
        }
        #endregion

        #region 5. Nullable Property Test
        [Fact]
        public void Test_05_NullableProperties_ShouldBeIgnoredWhenNullOrDeserializedCorrectly()
        {
            var statusMsg = new AuthStatusMessage
            {
                Status = "FAILED",
                ErrorCode = "AUTH_FAILED",
                Message = null // Should be omitted or null
            };

            string json = ProtocolSerialization.Serialize(statusMsg);

            // By default, default ignore condition when writing null is set.
            Assert.DoesNotContain("\"message\"", json);

            string jsonWithNull = "{\"type\":\"AUTH_STATUS\",\"status\":\"FAILED\",\"errorCode\":\"AUTH_FAILED\",\"message\":null}";
            var deserialized = ProtocolSerialization.Deserialize<AuthStatusMessage>(jsonWithNull);
            Assert.NotNull(deserialized);
            Assert.Null(deserialized.Message);
            Assert.Equal("AUTH_FAILED", deserialized.ErrorCode);
        }
        #endregion

        #region 6. DateTime UTC Serialization Test
        [Fact]
        public void Test_06_DateTimeUTC_ShouldFormatCorrectly()
        {
            var utcTime = new DateTime(2026, 8, 10, 15, 30, 45, DateTimeKind.Utc);
            var response = new DiscoveryResponse { Timestamp = utcTime };

            string json = ProtocolSerialization.Serialize(response);

            // Expect UTC iso format
            Assert.Contains("\"timestamp\":\"2026-08-10T15:30:45Z\"", json);
        }
        #endregion

        #region 7. Decimal Precision Test
        [Fact]
        public void Test_07_DecimalPrecision_ShouldBePreservedForFinancials()
        {
            var payload = new StartSessionPayload
            {
                UserId = "user-1",
                Username = "Alice",
                DurationMinutes = 60,
                RatePerHour = 1250.75m // financial decimal value
            };

            string json = ProtocolSerialization.Serialize(payload);

            Assert.Contains("\"ratePerHour\":1250.75", json);

            var deserialized = ProtocolSerialization.Deserialize<StartSessionPayload>(json);
            Assert.NotNull(deserialized);
            Assert.Equal(1250.75m, deserialized.RatePerHour);
        }
        #endregion

        #region 8. Discovery Contract Test
        [Fact]
        public void Test_08_DiscoveryContracts_ShouldSerializeAndDeserializeCorrectly()
        {
            var request = new DiscoveryRequest { ClientId = "client-99" };
            string reqJson = ProtocolSerialization.Serialize(request);
            Assert.Contains("\"type\":\"DISCOVER_SAYRA_SERVER\"", reqJson);
            Assert.Contains("\"clientId\":\"client-99\"", reqJson);

            string respJson = "{\"type\":\"SAYRA_SERVER_RESPONSE\",\"serverId\":\"srv-1\",\"serverName\":\"Sayra\",\"ip\":\"127.0.0.1\",\"tcpPort\":5000,\"apiPort\":80,\"version\":\"1.0\",\"timestamp\":\"2026-08-10T12:00:00Z\",\"nonce\":\"n1\",\"signature\":\"sig1\"}";
            var response = ProtocolSerialization.Deserialize<DiscoveryResponse>(respJson);

            Assert.NotNull(response);
            Assert.Equal("SAYRA_SERVER_RESPONSE", response.Type);
            Assert.Equal("srv-1", response.ServerId);
            Assert.Equal(5000, response.TcpPort);
        }
        #endregion

        #region 9. Authentication Contract Test
        [Fact]
        public void Test_09_AuthenticationContracts_ShouldSerializeAndDeserializeCorrectly()
        {
            // Challenge
            var challenge = new AuthChallengeMessage { Challenge = "test-challenge-123" };
            string chalJson = ProtocolSerialization.Serialize(challenge);
            Assert.Contains("\"type\":\"AUTH_CHALLENGE\"", chalJson);
            Assert.Contains("\"challenge\":\"test-challenge-123\"", chalJson);

            // Response
            var response = new AuthResponseMessage
            {
                Response = "hmac-sig-xyz",
                EncryptedSessionKey = "session-key-encrypted",
                Iv = "iv-base64",
                PcId = "PC-01",
                Hostname = "DELL-WORKSTATION"
            };
            string respJson = ProtocolSerialization.Serialize(response);
            Assert.Contains("\"type\":\"AUTH_RESPONSE\"", respJson);
            Assert.Contains("\"response\":\"hmac-sig-xyz\"", respJson);
            // Verify our robust dual-property map for client compatibility
            Assert.Contains("\"hmac\":\"hmac-sig-xyz\"", respJson);

            // Status
            var status = new AuthStatusMessage { Status = "SUCCESS", Message = "Authenticated successfully" };
            string statusJson = ProtocolSerialization.Serialize(status);
            Assert.Contains("\"type\":\"AUTH_STATUS\"", statusJson);
            Assert.Contains("\"status\":\"SUCCESS\"", statusJson);
        }
        #endregion

        #region 10. Secure Envelope Contract Test
        [Fact]
        public void Test_10_SecureEnvelopeContract_ShouldSerializeAndDeserializeCorrectly()
        {
            var envelope = new SecureMessageEnvelope
            {
                Payload = "encrypted-payload-string",
                Signature = "hmac-signature",
                Timestamp = "2026-08-10T15:00:00Z"
            };

            string json = ProtocolSerialization.Serialize(envelope);
            Assert.Contains("\"payload\":\"encrypted-payload-string\"", json);
            Assert.Contains("\"signature\":\"hmac-signature\"", json);

            var deserialized = ProtocolSerialization.Deserialize<SecureMessageEnvelope>(json);
            Assert.NotNull(deserialized);
            Assert.Equal("encrypted-payload-string", deserialized.Payload);
        }
        #endregion

        #region 11. Command Contract Test
        [Fact]
        public void Test_11_CommandContracts_ShouldSerializeAndDeserializeCorrectly()
        {
            // START_SESSION Command
            var startCmd = new CommandMessage<StartSessionPayload>
            {
                CommandId = "cmd-001",
                Type = "START_SESSION",
                Payload = new StartSessionPayload
                {
                    UserId = "usr-45",
                    Username = "JohnDoe",
                    DurationMinutes = 120,
                    RatePerHour = 10.50m
                },
                CorrelationId = "corr-101",
                Timestamp = new DateTime(2026, 8, 10, 16, 0, 0, DateTimeKind.Utc)
            };

            string json = ProtocolSerialization.Serialize(startCmd);
            Assert.Contains("\"type\":\"START_SESSION\"", json);
            Assert.Contains("\"username\":\"JohnDoe\"", json);

            var deserialized = ProtocolSerialization.Deserialize<CommandMessage<StartSessionPayload>>(json);
            Assert.NotNull(deserialized);
            Assert.Equal("cmd-001", deserialized.CommandId);
            Assert.Equal(10.50m, deserialized.Payload!.RatePerHour);

            // RUN_APP Command
            var runCmd = new CommandMessage<RunAppPayload>
            {
                Type = "RUN_APP",
                Payload = new RunAppPayload { ExecutablePath = "calc.exe", Arguments = "/q" }
            };
            string runJson = ProtocolSerialization.Serialize(runCmd);
            Assert.Contains("\"executablePath\":\"calc.exe\"", runJson);

            // KILL_APP Command
            var killCmd = new CommandMessage<KillAppPayload>
            {
                Type = "KILL_APP",
                Payload = new KillAppPayload { ProcessId = 1234, ProcessName = "notepad" }
            };
            string killJson = ProtocolSerialization.Serialize(killCmd);
            Assert.Contains("\"processId\":1234", killJson);
        }
        #endregion

        #region 12. Execution Result Test
        [Fact]
        public void Test_12_ExecutionResultMessage_ShouldSerializeAndDeserializeCorrectly()
        {
            var result = new ExecutionResultMessage
            {
                CommandId = "cmd-123",
                Status = "Executed",
                Message = "Command executed successfully",
                Result = new { pid = 5678 },
                Timestamp = new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc),
                CorrelationId = "corr-789"
            };

            string json = ProtocolSerialization.Serialize(result);
            Assert.Contains("\"commandId\":\"cmd-123\"", json);
            Assert.Contains("\"status\":\"Executed\"", json);
            Assert.Contains("\"pid\":5678", json);

            var deserialized = ProtocolSerialization.Deserialize<ExecutionResultMessage>(json);
            Assert.NotNull(deserialized);
            Assert.Equal("Executed", deserialized.Status);
        }
        #endregion

        #region 13. Telemetry Contract Test
        [Fact]
        public void Test_13_TelemetryModel_ShouldSerializeAndDeserializeCorrectly()
        {
            var telemetry = new TelemetryModel
            {
                Cpu = 45.2,
                Ram = 8192.5,
                Uptime = 3600.0,
                Timestamp = new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc),
                RunningGameName = "Counter-Strike 2",
                RunningGamePid = 9988,
                RunningGameCpu = 35.5,
                RunningGameRam = 4096.0,
                RunningGameDuration = 1800.0,
                TotalLaunches = 10,
                TotalCrashes = 1,
                TotalRestarts = 2
            };

            string json = ProtocolSerialization.Serialize(telemetry);
            Assert.Contains("\"cpu\":45.2", json);
            Assert.Contains("\"runningGameName\":\"Counter-Strike 2\"", json);
            Assert.Contains("\"totalLaunches\":10", json);

            var deserialized = ProtocolSerialization.Deserialize<TelemetryModel>(json);
            Assert.NotNull(deserialized);
            Assert.Equal(45.2, deserialized.Cpu);
            Assert.Equal("Counter-Strike 2", deserialized.RunningGameName);
        }
        #endregion

        #region 14. Event Contract Test
        [Fact]
        public void Test_14_EventContracts_ShouldSerializeAndDeserializeCorrectly()
        {
            var ev = new EventMessage
            {
                EventType = "SECURITY_BREACH_DETECTED",
                EventId = "evt-1122",
                PcId = "PC-33",
                Timestamp = new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc),
                Details = new Dictionary<string, string> { { "reason", "Unauthorized process injection detected" } }
            };

            string json = ProtocolSerialization.Serialize(ev);
            Assert.Contains("\"eventType\":\"SECURITY_BREACH_DETECTED\"", json);
            Assert.Contains("\"reason\":\"Unauthorized process injection detected\"", json);

            var deserialized = ProtocolSerialization.Deserialize<EventMessage>(json);
            Assert.NotNull(deserialized);
            Assert.Equal("SECURITY_BREACH_DETECTED", deserialized.EventType);
        }
        #endregion

        #region 15. Configuration Contract Test
        [Fact]
        public void Test_15_ConfigurationContracts_ShouldSerializeAndDeserializeCorrectly()
        {
            var package = new ConfigurationPackageContract
            {
                Version = "1.0.4",
                CreatedAt = new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc),
                IssuedBy = "Admin",
                Hash = "sha256-hash",
                Signature = "admin-sig",
                Payload = new { maxUsageTime = 120 },
                PayloadType = "Full",
                TargetClient = "PC-ALL",
                TargetGroup = "VIP"
            };

            string json = ProtocolSerialization.Serialize(package);
            Assert.Contains("\"version\":\"1.0.4\"", json);
            Assert.Contains("\"payloadType\":\"Full\"", json);

            var deserialized = ProtocolSerialization.Deserialize<ConfigurationPackageContract>(json);
            Assert.NotNull(deserialized);
            Assert.Equal("1.0.4", deserialized.Version);

            // Delta
            var delta = new ConfigurationDelta
            {
                Path = "rates.vip",
                Op = "replace",
                Value = 15.0
            };
            string deltaJson = ProtocolSerialization.Serialize(delta);
            Assert.Contains("\"path\":\"rates.vip\"", deltaJson);
            Assert.Contains("\"op\":\"replace\"", deltaJson);
        }
        #endregion

        #region 16. Update Manifest Test
        [Fact]
        public void Test_16_UpdateManifest_ShouldSerializeAndDeserializeCorrectly()
        {
            var manifest = new UpdateManifest
            {
                Version = "1.2.0",
                ReleaseNotes = "Performance improvements",
                PackageUrl = "https://sayra.net/updates/1.2.0.zip",
                Checksum = "sha256-checksum",
                Signature = "sig-123"
            };

            string json = ProtocolSerialization.Serialize(manifest);
            Assert.Contains("\"version\":\"1.2.0\"", json);
            Assert.Contains("\"packageUrl\":\"https://sayra.net/updates/1.2.0.zip\"", json);

            var deserialized = ProtocolSerialization.Deserialize<UpdateManifest>(json);
            Assert.NotNull(deserialized);
            Assert.Equal("1.2.0", deserialized.Version);
        }
        #endregion

        #region 17. Error Contract Test
        [Fact]
        public void Test_17_ErrorContract_ShouldSerializeAndDeserializeCorrectlyAndBeSecure()
        {
            var error = new ErrorResponseContract
            {
                Code = "SESSION_EXPIRED",
                Message = "Your session has expired.",
                CorrelationId = "corr-44",
                Timestamp = new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc),
                Details = "Full diagnostics for debug"
            };

            string json = ProtocolSerialization.Serialize(error);
            Assert.Contains("\"code\":\"SESSION_EXPIRED\"", json);

            // Ensure no sensitive cryptographic materials are inside the JSON
            Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("sessionKey", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);

            var deserialized = ProtocolSerialization.Deserialize<ErrorResponseContract>(json);
            Assert.NotNull(deserialized);
            Assert.Equal("SESSION_EXPIRED", deserialized.Code);
        }
        #endregion

        #region 18. Backward Compatibility Test & Golden Contracts
        [Fact]
        public void Test_18_BackwardCompatibilityAndGoldenContracts()
        {
            // Golden JSON samples reflecting Client's exact schema
            string goldenDiscoveryResponse = "{\"type\":\"SAYRA_SERVER_RESPONSE\",\"serverId\":\"srv-gold\",\"serverName\":\"GoldenSayra\",\"ip\":\"127.0.0.1\",\"tcpPort\":5000,\"apiPort\":8080,\"version\":\"1.0.0\",\"timestamp\":\"2026-08-10T12:00:00Z\",\"nonce\":\"gold-nonce\",\"signature\":\"gold-signature\"}";
            var dResponse = ProtocolSerialization.Deserialize<DiscoveryResponse>(goldenDiscoveryResponse);
            Assert.NotNull(dResponse);
            Assert.Equal("SAYRA_SERVER_RESPONSE", dResponse.Type);
            Assert.Equal("srv-gold", dResponse.ServerId);

            // Existing client might send PascalCase or unknown properties; verify robustness
            string legacyClientDiscovery = "{\"Type\":\"SAYRA_SERVER_RESPONSE\",\"ServerId\":\"srv-gold\",\"serverName\":\"GoldenSayra\",\"extraFieldIgnored\":\"someJunk\"}";
            var dLegacy = ProtocolSerialization.Deserialize<DiscoveryResponse>(legacyClientDiscovery);
            Assert.NotNull(dLegacy);
            Assert.Equal("SAYRA_SERVER_RESPONSE", dLegacy.Type);
            Assert.Equal("srv-gold", dLegacy.ServerId);

            // Golden AuthResponse matching Client
            string goldenAuthResponse = "{\"type\":\"AUTH_RESPONSE\",\"response\":\"hmac-test-sig\",\"encryptedSessionKey\":\"encrypted-key\",\"iv\":\"iv-data\",\"pcId\":\"PC-GOLDEN\",\"hostname\":\"DESKTOP-GOLDEN\"}";
            var authResponse = ProtocolSerialization.Deserialize<AuthResponseMessage>(goldenAuthResponse);
            Assert.NotNull(authResponse);
            Assert.Equal("AUTH_RESPONSE", authResponse.Type);
            Assert.Equal("hmac-test-sig", authResponse.Response);
            // Verify our robust legacy map also read it as Hmac
            Assert.Equal("hmac-test-sig", authResponse.Hmac);
        }
        #endregion
    }
}
