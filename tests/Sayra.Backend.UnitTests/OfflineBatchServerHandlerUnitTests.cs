using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Sayra.Backend.Application.OfflineQueue;
using Sayra.Backend.Contracts;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class OfflineBatchServerHandlerUnitTests
    {
        private readonly IngestOfflineBatchCommandHandler _handler;

        public OfflineBatchServerHandlerUnitTests()
        {
            _handler = new IngestOfflineBatchCommandHandler(NullLogger<IngestOfflineBatchCommandHandler>.Instance);
        }

        [Fact]
        public async Task HandleAsync_IdentityMismatch_RejectsWithSecurityViolation()
        {
            var req = new OfflineBatchRequest
            {
                BatchId = Guid.NewGuid().ToString("N"),
                ClientId = "PC-999",
                WorkstationId = "PC-999",
                Items = new List<OfflineQueueItem>
                {
                    new OfflineQueueItem { EventId = Guid.NewGuid().ToString("D"), EventType = "TEST", Payload = "{}" }
                }
            };

            var command = new IngestOfflineBatchCommand("conn-1", "PC-001", req);
            var result = await _handler.HandleAsync(command);

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            var ack = result.Value.Acknowledgment;
            Assert.False(ack.Success);
            Assert.Contains("SECURITY_VIOLATION", ack.ErrorMessage);
        }

        [Fact]
        public async Task HandleAsync_ValidBatch_AcceptsItemsAndReturnsAcknowledgedIds()
        {
            var id1 = Guid.NewGuid().ToString("D");
            var id2 = Guid.NewGuid().ToString("D");

            var req = new OfflineBatchRequest
            {
                BatchId = Guid.NewGuid().ToString("N"),
                ClientId = "PC-001",
                WorkstationId = "WS-001",
                Items = new List<OfflineQueueItem>
                {
                    new OfflineQueueItem { EventId = id1, EventType = "SESSION_COMMAND_REQUEST", Payload = "{}" },
                    new OfflineQueueItem { EventId = id2, EventType = "SECURITY_EVENT", Payload = "{}" }
                }
            };

            var command = new IngestOfflineBatchCommand("conn-1", "PC-001", req);
            var result = await _handler.HandleAsync(command);

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            var ack = result.Value.Acknowledgment;
            Assert.True(ack.Success);
            Assert.Equal(2, ack.ProcessedCount);
            Assert.Contains(id1, ack.AcknowledgedEventIds);
            Assert.Contains(id2, ack.AcknowledgedEventIds);
            Assert.Empty(ack.RejectedEventIds);
        }

        [Fact]
        public async Task HandleAsync_InvalidEventId_RejectsIndividualEvent()
        {
            var validId = Guid.NewGuid().ToString("D");
            var invalidId = "not-a-valid-guid";

            var req = new OfflineBatchRequest
            {
                BatchId = Guid.NewGuid().ToString("N"),
                ClientId = "PC-001",
                WorkstationId = "WS-001",
                Items = new List<OfflineQueueItem>
                {
                    new OfflineQueueItem { EventId = validId, EventType = "SESSION_COMMAND_REQUEST", Payload = "{}" },
                    new OfflineQueueItem { EventId = invalidId, EventType = "SECURITY_EVENT", Payload = "{}" }
                }
            };

            var command = new IngestOfflineBatchCommand("conn-1", "PC-001", req);
            var result = await _handler.HandleAsync(command);

            Assert.True(result.IsSuccess);
            var ack = result.Value.Acknowledgment;
            Assert.True(ack.Success);
            Assert.Equal(1, ack.ProcessedCount);
            Assert.Contains(validId, ack.AcknowledgedEventIds);
            Assert.Contains(invalidId, ack.RejectedEventIds);
        }

        [Fact]
        public async Task HandleAsync_OversizedBatch_RejectsBatch()
        {
            var items = new List<OfflineQueueItem>();
            for (int i = 0; i < 101; i++)
            {
                items.Add(new OfflineQueueItem { EventId = Guid.NewGuid().ToString("D"), EventType = "TEST", Payload = "{}" });
            }

            var req = new OfflineBatchRequest
            {
                BatchId = Guid.NewGuid().ToString("N"),
                ClientId = "PC-001",
                WorkstationId = "WS-001",
                Items = items
            };

            var command = new IngestOfflineBatchCommand("conn-1", "PC-001", req);
            var result = await _handler.HandleAsync(command);

            Assert.True(result.IsSuccess);
            var ack = result.Value.Acknowledgment;
            Assert.False(ack.Success);
            Assert.Contains("exceeds maximum limit", ack.ErrorMessage);
        }
    }
}
