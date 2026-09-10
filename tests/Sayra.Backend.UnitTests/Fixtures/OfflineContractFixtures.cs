using System;
using System.Collections.Generic;
using Sayra.Backend.Contracts;

namespace Sayra.Backend.UnitTests.Fixtures
{
    public static class OfflineContractFixtures
    {
        public const string FixedEventId1 = "11111111-1111-1111-1111-111111111111";
        public const string FixedEventId2 = "22222222-2222-2222-2222-222222222222";
        public const string FixedEventId3 = "33333333-3333-3333-3333-333333333333";
        public const string FixedBatchId = "batch-99999999-aaaa-bbbb-cccc-dddddddddddd";
        public const string AuthenticatedPcId = "PC-001";
        public const string SpoofedPcId = "PC-999";

        public static ClientEventEnvelopeDto CreateValidClientEventEnvelope()
        {
            return new ClientEventEnvelopeDto
            {
                EventId = FixedEventId1,
                EventType = ClientEventType.ApplicationCrashed,
                ClientId = AuthenticatedPcId,
                WorkstationId = AuthenticatedPcId,
                SessionId = "55555555-5555-5555-5555-555555555555",
                CorrelationId = "corr-12345",
                SequenceNumber = 101,
                ContractVersion = "1.0",
                OccurredAt = new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc),
                Payload = "{\"processName\":\"Game.exe\",\"exitCode\":-1,\"exception\":\"AccessViolationException\"}"
            };
        }

        public const string ValidClientEventEnvelopeJson = @"{
            ""eventId"": ""11111111-1111-1111-1111-111111111111"",
            ""eventType"": ""APPLICATION_CRASHED"",
            ""clientId"": ""PC-001"",
            ""workstationId"": ""PC-001"",
            ""sessionId"": ""55555555-5555-5555-5555-555555555555"",
            ""correlationId"": ""corr-12345"",
            ""sequenceNumber"": 101,
            ""contractVersion"": ""1.0"",
            ""occurredAt"": ""2026-09-10T12:00:00Z"",
            ""payload"": ""{\""processName\"":\""Game.exe\"",\""exitCode\"":-1,\""exception\"":\""AccessViolationException\""}""
        }";

        public static ClientEventEnvelopeDto CreateIdentityMismatchEnvelope()
        {
            return new ClientEventEnvelopeDto
            {
                EventId = FixedEventId2,
                EventType = ClientEventType.SecurityEvent,
                ClientId = SpoofedPcId, // Identity mismatch against connection PC-001
                WorkstationId = SpoofedPcId,
                CorrelationId = "corr-spoofed",
                SequenceNumber = 102,
                ContractVersion = "1.0",
                OccurredAt = DateTime.UtcNow,
                Payload = "{\"violationType\":\"UNAUTHORIZED_REGISTRY_WRITE\"}"
            };
        }

        public static ClientEventEnvelopeDto CreateExpiredEnvelope()
        {
            return new ClientEventEnvelopeDto
            {
                EventId = FixedEventId3,
                EventType = ClientEventType.ClientStopped,
                ClientId = AuthenticatedPcId,
                WorkstationId = AuthenticatedPcId,
                CorrelationId = "corr-expired",
                SequenceNumber = 1,
                ContractVersion = "1.0",
                OccurredAt = DateTime.UtcNow.AddDays(-40), // 40 days old (> 30 days retention threshold)
                Payload = "{\"shutdownReason\":\"Maintenance\"}"
            };
        }

        public static ClientEventEnvelopeDto CreateFutureTimestampEnvelope()
        {
            return new ClientEventEnvelopeDto
            {
                EventId = Guid.NewGuid().ToString("D"),
                EventType = ClientEventType.WorkstationStateChanged,
                ClientId = AuthenticatedPcId,
                WorkstationId = AuthenticatedPcId,
                CorrelationId = "corr-future",
                SequenceNumber = 103,
                ContractVersion = "1.0",
                OccurredAt = DateTime.UtcNow.AddHours(2), // 2 hours in future (clock skew)
                Payload = "{\"newState\":\"LOCKED\"}"
            };
        }

        public static OfflineBatchRequest CreateValidOfflineBatchRequest()
        {
            return new OfflineBatchRequest
            {
                BatchId = FixedBatchId,
                ClientId = AuthenticatedPcId,
                WorkstationId = AuthenticatedPcId,
                ContractVersion = "1.0",
                Items = new List<OfflineQueueItem>
                {
                    new OfflineQueueItem
                    {
                        EventId = FixedEventId1,
                        EventType = ClientEventType.SessionRuntimeEvent,
                        SequenceNumber = 101,
                        ReliabilityClass = EventReliabilityClass.Critical,
                        ContractVersion = "1.0",
                        Payload = new { consumedSeconds = 3600, status = "ACTIVE" },
                        Timestamp = new DateTime(2026, 9, 10, 10, 0, 0, DateTimeKind.Utc)
                    },
                    new OfflineQueueItem
                    {
                        EventId = FixedEventId2,
                        EventType = ClientEventType.ApplicationCrashed,
                        SequenceNumber = 102,
                        ReliabilityClass = EventReliabilityClass.Important,
                        ContractVersion = "1.0",
                        Payload = new { processName = "CrashApp.exe" },
                        Timestamp = new DateTime(2026, 9, 10, 10, 5, 0, DateTimeKind.Utc)
                    },
                    new OfflineQueueItem
                    {
                        EventId = FixedEventId3,
                        EventType = ClientEventType.DeviceChanged,
                        SequenceNumber = 103,
                        ReliabilityClass = EventReliabilityClass.Normal,
                        ContractVersion = "1.0",
                        Payload = new { deviceName = "USB Gaming Mouse" },
                        Timestamp = new DateTime(2026, 9, 10, 10, 10, 0, DateTimeKind.Utc)
                    }
                }
            };
        }

        public const string ValidOfflineBatchRequestJson = @"{
            ""batchId"": ""batch-99999999-aaaa-bbbb-cccc-dddddddddddd"",
            ""clientId"": ""PC-001"",
            ""workstationId"": ""PC-001"",
            ""contractVersion"": ""1.0"",
            ""items"": [
                {
                    ""eventId"": ""11111111-1111-1111-1111-111111111111"",
                    ""eventType"": ""SESSION_RUNTIME_EVENT"",
                    ""sequenceNumber"": 101,
                    ""reliabilityClass"": ""CRITICAL"",
                    ""contractVersion"": ""1.0"",
                    ""payload"": { ""consumedSeconds"": 3600, ""status"": ""ACTIVE"" },
                    ""timestamp"": ""2026-09-10T10:00:00Z""
                },
                {
                    ""eventId"": ""22222222-2222-2222-2222-222222222222"",
                    ""eventType"": ""APPLICATION_CRASHED"",
                    ""sequenceNumber"": 102,
                    ""reliabilityClass"": ""IMPORTANT"",
                    ""contractVersion"": ""1.0"",
                    ""payload"": { ""processName"": ""CrashApp.exe"" },
                    ""timestamp"": ""2026-09-10T10:05:00Z""
                }
            ]
        }";

        public static OfflineBatchAcknowledgment CreateValidBatchAcknowledgment()
        {
            return new OfflineBatchAcknowledgment
            {
                BatchId = FixedBatchId,
                ProcessedCount = 2,
                Success = true,
                AcknowledgedEventIds = new List<string> { FixedEventId1, FixedEventId2 },
                RejectedEventIds = new List<string>(),
                ErrorMessage = null
            };
        }

        public static SessionCommandPayload CreateCriticalSessionCommandPayload()
        {
            return new SessionCommandPayload
            {
                Action = "EXTEND",
                SessionId = Guid.Parse("55555555-5555-5555-5555-555555555555"),
                GamerId = Guid.Parse("66666666-6666-6666-6666-666666666666"),
                WorkstationId = Guid.Parse("77777777-7777-7777-7777-777777777777"),
                AdditionalMinutes = 60,
                IdempotencyKey = "ext-idemp-key-001"
            };
        }
    }
}
