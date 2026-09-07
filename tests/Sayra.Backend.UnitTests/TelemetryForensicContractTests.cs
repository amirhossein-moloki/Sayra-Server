using System;
using System.Text.Json;
using Sayra.Backend.Contracts;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class TelemetryForensicContractTests
    {
        [Fact]
        public void TelemetryModel_SerializationAndDeserialization_MatchesContract()
        {
            var model = new TelemetryModel
            {
                Cpu = 45.5,
                Ram = 8192.0,
                Uptime = 36000.0,
                Timestamp = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc),
                RunningGameName = "Cyberpunk 2077",
                RunningGamePid = 1234,
                RunningGameCpu = 35.0,
                RunningGameRam = 4096.0,
                RunningGameDuration = 1800.0,
                TotalLaunches = 10,
                TotalCrashes = 1,
                TotalRestarts = 2
            };

            string json = ProtocolSerialization.Serialize(model);
            Assert.Contains("\"cpu\":45.5", json);
            Assert.Contains("\"ram\":8192", json);
            Assert.Contains("\"runningGameName\":\"Cyberpunk 2077\"", json);

            var deserialized = ProtocolSerialization.Deserialize<TelemetryModel>(json);
            Assert.NotNull(deserialized);
            Assert.Equal(45.5, deserialized.Cpu);
            Assert.Equal(8192.0, deserialized.Ram);
            Assert.Equal(36000.0, deserialized.Uptime);
            Assert.Equal("Cyberpunk 2077", deserialized.RunningGameName);
            Assert.Equal(1234, deserialized.RunningGamePid);
            Assert.Equal(10, deserialized.TotalLaunches);
            Assert.Equal(1, deserialized.TotalCrashes);
            Assert.Equal(2, deserialized.TotalRestarts);
        }

        [Fact]
        public void HeartbeatMessage_And_PongMessage_Serialization_MatchesContract()
        {
            var heartbeat = new HeartbeatMessage
            {
                Type = "HEARTBEAT",
                PcId = "PC-101",
                Timestamp = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc)
            };

            string json = ProtocolSerialization.Serialize(heartbeat);
            Assert.Contains("\"type\":\"HEARTBEAT\"", json);
            Assert.Contains("\"pcId\":\"PC-101\"", json);

            var deserializedHeartbeat = ProtocolSerialization.Deserialize<HeartbeatMessage>(json);
            Assert.NotNull(deserializedHeartbeat);
            Assert.Equal("HEARTBEAT", deserializedHeartbeat.Type);
            Assert.Equal("PC-101", deserializedHeartbeat.PcId);

            var pong = new PongMessage
            {
                Type = "PONG",
                Timestamp = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc)
            };

            string pongJson = ProtocolSerialization.Serialize(pong);
            Assert.Contains("\"type\":\"PONG\"", pongJson);

            var deserializedPong = ProtocolSerialization.Deserialize<PongMessage>(pongJson);
            Assert.NotNull(deserializedPong);
            Assert.Equal("PONG", deserializedPong.Type);
        }

        [Fact]
        public void ClientEventEnvelopeDto_Serialization_MatchesContract()
        {
            var evt = new ClientEventEnvelopeDto
            {
                EventId = Guid.NewGuid().ToString("N"),
                EventType = ClientEventType.ApplicationCrashed,
                ClientId = "PC-101",
                WorkstationId = "PC-101",
                SessionId = Guid.NewGuid().ToString(),
                CorrelationId = Guid.NewGuid().ToString("N"),
                OccurredAt = DateTime.UtcNow,
                Payload = "{\"exitCode\": -1}"
            };

            string json = ProtocolSerialization.Serialize(evt);
            Assert.Contains("\"eventType\":\"APPLICATION_CRASHED\"", json);
            Assert.Contains("\"clientId\":\"PC-101\"", json);

            var deserialized = ProtocolSerialization.Deserialize<ClientEventEnvelopeDto>(json);
            Assert.NotNull(deserialized);
            Assert.Equal(evt.EventId, deserialized.EventId);
            Assert.Equal(ClientEventType.ApplicationCrashed, deserialized.EventType);
            Assert.Equal("PC-101", deserialized.ClientId);
            Assert.Equal("PC-101", deserialized.WorkstationId);
        }

        [Fact]
        public void CommunicationMessage_Envelope_Serialization_MatchesContract()
        {
            var telemetry = new TelemetryModel
            {
                Cpu = 12.5,
                Ram = 4096.0,
                Uptime = 7200.0,
                Timestamp = DateTime.UtcNow
            };

            var msg = CommunicationMessage<TelemetryModel>.Create(
                "TELEMETRY",
                telemetry,
                correlationId: "corr-123",
                senderId: "PC-101");

            string json = ProtocolSerialization.Serialize(msg);
            Assert.Contains("\"messageType\":\"TELEMETRY\"", json);
            Assert.Contains("\"senderId\":\"PC-101\"", json);

            var deserialized = ProtocolSerialization.Deserialize<CommunicationMessage<TelemetryModel>>(json);
            Assert.NotNull(deserialized);
            Assert.NotNull(deserialized.Metadata);
            Assert.Equal("TELEMETRY", deserialized.Metadata.MessageType);
            Assert.Equal("PC-101", deserialized.Metadata.SenderId);
            Assert.NotNull(deserialized.Payload);
            Assert.Equal(12.5, deserialized.Payload.Cpu);
        }
    }
}
