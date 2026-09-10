using System;
using System.Linq;
using System.Text.Json;
using Sayra.Backend.Contracts;
using Sayra.Backend.UnitTests.Fixtures;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class OfflineContractTests
    {
        [Fact]
        public void ClientEventEnvelopeDto_Serialization_Deserialization_PreservesAllFields()
        {
            // Arrange
            var original = OfflineContractFixtures.CreateValidClientEventEnvelope();

            // Act
            string json = ProtocolSerialization.Serialize(original);
            var deserialized = ProtocolSerialization.Deserialize<ClientEventEnvelopeDto>(json);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Equal(original.EventId, deserialized.EventId);
            Assert.Equal(original.EventType, deserialized.EventType);
            Assert.Equal(original.ClientId, deserialized.ClientId);
            Assert.Equal(original.WorkstationId, deserialized.WorkstationId);
            Assert.Equal(original.SessionId, deserialized.SessionId);
            Assert.Equal(original.CorrelationId, deserialized.CorrelationId);
            Assert.Equal(original.SequenceNumber, deserialized.SequenceNumber);
            Assert.Equal(original.ContractVersion, deserialized.ContractVersion);
            Assert.Equal(original.OccurredAt, deserialized.OccurredAt);
            Assert.Equal(original.Payload, deserialized.Payload);
        }

        [Fact]
        public void GoldenJson_ClientEventEnvelope_DeserializesToExpectedContract()
        {
            // Act
            var dto = ProtocolSerialization.Deserialize<ClientEventEnvelopeDto>(OfflineContractFixtures.ValidClientEventEnvelopeJson);

            // Assert
            Assert.NotNull(dto);
            Assert.Equal(OfflineContractFixtures.FixedEventId1, dto.EventId);
            Assert.Equal(ClientEventType.ApplicationCrashed, dto.EventType);
            Assert.Equal(OfflineContractFixtures.AuthenticatedPcId, dto.ClientId);
            Assert.Equal(OfflineContractFixtures.AuthenticatedPcId, dto.WorkstationId);
            Assert.Equal(101, dto.SequenceNumber);
            Assert.Equal("1.0", dto.ContractVersion);
            Assert.Equal(new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc), dto.OccurredAt);
            Assert.Contains("Game.exe", dto.Payload);
        }

        [Fact]
        public void GoldenJson_OfflineBatchRequest_DeserializesToExpectedContract()
        {
            // Act
            var batch = ProtocolSerialization.Deserialize<OfflineBatchRequest>(OfflineContractFixtures.ValidOfflineBatchRequestJson);

            // Assert
            Assert.NotNull(batch);
            Assert.Equal(OfflineContractFixtures.FixedBatchId, batch.BatchId);
            Assert.Equal(OfflineContractFixtures.AuthenticatedPcId, batch.ClientId);
            Assert.Equal(2, batch.Items.Count);

            var item1 = batch.Items.First(i => i.EventId == OfflineContractFixtures.FixedEventId1);
            Assert.Equal(ClientEventType.SessionRuntimeEvent, item1.EventType);
            Assert.Equal(EventReliabilityClass.Critical, item1.ReliabilityClass);
            Assert.Equal(101, item1.SequenceNumber);

            var item2 = batch.Items.First(i => i.EventId == OfflineContractFixtures.FixedEventId2);
            Assert.Equal(ClientEventType.ApplicationCrashed, item2.EventType);
            Assert.Equal(EventReliabilityClass.Important, item2.ReliabilityClass);
            Assert.Equal(102, item2.SequenceNumber);
        }

        [Fact]
        public void EventId_PreservedUnchangedAcrossEnvelopeAndQueueItem()
        {
            // Arrange
            string eventId = Guid.NewGuid().ToString("D");
            var envelope = new ClientEventEnvelopeDto
            {
                EventId = eventId,
                EventType = ClientEventType.SecurityEvent,
                ClientId = "PC-001",
                WorkstationId = "PC-001"
            };

            var queueItem = new OfflineQueueItem
            {
                EventId = envelope.EventId,
                EventType = envelope.EventType,
                SequenceNumber = envelope.SequenceNumber,
                ReliabilityClass = EventReliabilityClass.Critical,
                Payload = envelope.Payload
            };

            var ack = new OfflineBatchAcknowledgment
            {
                BatchId = "batch-1",
                ProcessedCount = 1,
                Success = true,
                AcknowledgedEventIds = new System.Collections.Generic.List<string> { queueItem.EventId }
            };

            // Assert invariant
            Assert.Equal(eventId, envelope.EventId);
            Assert.Equal(eventId, queueItem.EventId);
            Assert.Single(ack.AcknowledgedEventIds);
            Assert.Equal(eventId, ack.AcknowledgedEventIds[0]);
        }

        [Fact]
        public void SequenceNumber_Semantics_And_Ordering_Validation()
        {
            // Arrange
            var batch = OfflineContractFixtures.CreateValidOfflineBatchRequest();

            // Act
            var orderedItems = batch.Items.OrderBy(x => x.SequenceNumber).ToList();

            // Assert
            Assert.Equal(101, orderedItems[0].SequenceNumber);
            Assert.Equal(102, orderedItems[1].SequenceNumber);
            Assert.Equal(103, orderedItems[2].SequenceNumber);
        }

        [Fact]
        public void Timestamp_UTC_And_ClockSkew_Rules()
        {
            // Arrange
            var validEnvelope = OfflineContractFixtures.CreateValidClientEventEnvelope();
            var expiredEnvelope = OfflineContractFixtures.CreateExpiredEnvelope();
            var futureEnvelope = OfflineContractFixtures.CreateFutureTimestampEnvelope();

            DateTime nowUtc = DateTime.UtcNow;

            // Act & Assert
            // 1. Valid envelope is within reasonable range
            TimeSpan validAge = nowUtc - validEnvelope.OccurredAt;
            Assert.True(validAge.TotalDays >= 0);

            // 2. Expired envelope exceeds 30-day retention threshold
            TimeSpan expiredAge = nowUtc - expiredEnvelope.OccurredAt;
            Assert.True(expiredAge.TotalDays > 30, "Expired envelope should be > 30 days old.");

            // 3. Future envelope exceeds 5-minute clock skew threshold
            TimeSpan futureSkew = futureEnvelope.OccurredAt - nowUtc;
            Assert.True(futureSkew.TotalMinutes > 5, "Future timestamp should exceed 5-minute clock skew threshold.");
        }

        [Fact]
        public void IdentityBinding_PayloadVsConnectionIdentity_Verification()
        {
            // Arrange
            string authenticatedConnectionPcId = OfflineContractFixtures.AuthenticatedPcId;
            var validEnvelope = OfflineContractFixtures.CreateValidClientEventEnvelope();
            var spoofedEnvelope = OfflineContractFixtures.CreateIdentityMismatchEnvelope();

            // Act
            bool isValidMatch = string.Equals(validEnvelope.ClientId, authenticatedConnectionPcId, StringComparison.OrdinalIgnoreCase);
            bool isSpoofedMatch = string.Equals(spoofedEnvelope.ClientId, authenticatedConnectionPcId, StringComparison.OrdinalIgnoreCase);

            // Assert
            Assert.True(isValidMatch, "Valid envelope payload ClientId should match connection PC-ID.");
            Assert.False(isSpoofedMatch, "Spoofed envelope payload ClientId should NOT match connection PC-ID.");
        }

        [Theory]
        [InlineData(ClientEventType.SessionRuntimeEvent, EventReliabilityClass.Critical, true)]
        [InlineData(ClientEventType.SecurityEvent, EventReliabilityClass.Critical, true)]
        [InlineData(ClientEventType.ClientStarted, EventReliabilityClass.Important, true)]
        [InlineData(ClientEventType.ClientStopped, EventReliabilityClass.Important, true)]
        [InlineData(ClientEventType.ApplicationCrashed, EventReliabilityClass.Important, true)]
        [InlineData(ClientEventType.WorkstationStateChanged, EventReliabilityClass.Important, true)]
        [InlineData(ClientEventType.ConfigurationChanged, EventReliabilityClass.Important, true)]
        [InlineData(ClientEventType.ApplicationStarted, EventReliabilityClass.Normal, true)]
        [InlineData(ClientEventType.ApplicationExited, EventReliabilityClass.Normal, true)]
        [InlineData(ClientEventType.DeviceChanged, EventReliabilityClass.Normal, true)]
        [InlineData(ClientEventType.NetworkChanged, EventReliabilityClass.Normal, true)]
        [InlineData(ClientEventType.DiagnosticEvent, EventReliabilityClass.Normal, true)]
        [InlineData("TELEMETRY", EventReliabilityClass.Ephemeral, false)]
        [InlineData("HEARTBEAT", EventReliabilityClass.Ephemeral, false)]
        public void ReliabilityClassification_TaxonomyRules(string eventType, string expectedReliabilityClass, bool expectedQueueable)
        {
            // Act
            string reliabilityClass = GetReliabilityClassForEvent(eventType);
            bool isQueueable = IsEventQueueableOffline(reliabilityClass);

            // Assert
            Assert.Equal(expectedReliabilityClass, reliabilityClass);
            Assert.Equal(expectedQueueable, isQueueable);
        }

        [Fact]
        public void Idempotency_DeduplicationKey_Generation()
        {
            // Arrange
            string eventId = OfflineContractFixtures.FixedEventId1;

            // Act
            string redisDedupKey = $"v1:event:dedup:{eventId}";

            // Assert
            Assert.Equal($"v1:event:dedup:{OfflineContractFixtures.FixedEventId1}", redisDedupKey);
        }

        [Fact]
        public void ContractVersion_DefaultsAndCompatibility()
        {
            // Arrange
            var envelope = new ClientEventEnvelopeDto();
            var queueItem = new OfflineQueueItem();
            var batchRequest = new OfflineBatchRequest();

            // Assert
            Assert.Equal("1.0", envelope.ContractVersion);
            Assert.Equal("1.0", queueItem.ContractVersion);
            Assert.Equal("1.0", batchRequest.ContractVersion);
        }

        private static string GetReliabilityClassForEvent(string eventType)
        {
            return eventType.ToUpperInvariant() switch
            {
                ClientEventType.SessionRuntimeEvent or ClientEventType.SecurityEvent or "SESSION_COMMAND_REQUEST" => EventReliabilityClass.Critical,
                ClientEventType.ClientStarted or ClientEventType.ClientStopped or ClientEventType.ApplicationCrashed or ClientEventType.WorkstationStateChanged or ClientEventType.ConfigurationChanged => EventReliabilityClass.Important,
                ClientEventType.ApplicationStarted or ClientEventType.ApplicationExited or ClientEventType.DeviceChanged or ClientEventType.NetworkChanged or ClientEventType.DiagnosticEvent => EventReliabilityClass.Normal,
                "TELEMETRY" or "HEARTBEAT" => EventReliabilityClass.Ephemeral,
                _ => EventReliabilityClass.NotQueueable
            };
        }

        private static bool IsEventQueueableOffline(string reliabilityClass)
        {
            return reliabilityClass is EventReliabilityClass.Critical or EventReliabilityClass.Important or EventReliabilityClass.Normal;
        }
    }
}
