using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sayra.Backend.Application.OfflineQueue;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Infrastructure.OfflineQueue;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class OfflineSyncWorkerUnitTests
    {
        private readonly Mock<IDurableOfflineQueue> _mockQueue;
        private readonly OfflineSyncWorkerOptions _options;
        private readonly OfflineSyncWorker _worker;

        public OfflineSyncWorkerUnitTests()
        {
            _mockQueue = new Mock<IDurableOfflineQueue>();
            _options = new OfflineSyncWorkerOptions
            {
                MaxBatchSize = 10,
                SyncIntervalMs = 1000,
                BatchTimeoutSeconds = 30
            };

            _worker = new OfflineSyncWorker(
                _mockQueue.Object,
                Options.Create(_options),
                NullLogger<OfflineSyncWorker>.Instance);
        }

        [Fact]
        public async Task SynchronizeBatch_NoItemsInQueue_ReturnsSuccessWithZeroProcessed()
        {
            _mockQueue.Setup(q => q.ClaimBatchAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new List<DurableQueueItemEntity>());

            var ack = await _worker.SynchronizeBatchAsync(
                "PC-001",
                "WS-001",
                (req, ct) => Task.FromResult<OfflineBatchRequest?>(req) != null ? Task.FromResult<OfflineBatchAcknowledgment?>(new OfflineBatchAcknowledgment { Success = true, ProcessedCount = 0 }) : Task.FromResult<OfflineBatchAcknowledgment?>(null));

            Assert.NotNull(ack);
            Assert.True(ack.Success);
            Assert.Equal(0, ack.ProcessedCount);
        }

        [Fact]
        public async Task SynchronizeBatch_WithItems_ClaimsAndTransmitsCorrectBatch()
        {
            var eventId1 = Guid.NewGuid().ToString("D");
            var eventId2 = Guid.NewGuid().ToString("D");

            var items = new List<DurableQueueItemEntity>
            {
                new DurableQueueItemEntity
                {
                    EventId = eventId1,
                    EventType = "SESSION_COMMAND_REQUEST",
                    SequenceNumber = 1,
                    ReliabilityClass = "CRITICAL",
                    OccurredAt = DateTime.UtcNow,
                    Payload = "{\"sessionId\":\"S1\"}"
                },
                new DurableQueueItemEntity
                {
                    EventId = eventId2,
                    EventType = "WORKSTATION_STATE_CHANGED",
                    SequenceNumber = 2,
                    ReliabilityClass = "IMPORTANT",
                    OccurredAt = DateTime.UtcNow,
                    Payload = "{\"newState\":\"LOCKED\"}"
                }
            };

            _mockQueue.Setup(q => q.ClaimBatchAsync(10, It.IsAny<CancellationToken>()))
                      .ReturnsAsync(items);

            OfflineBatchRequest? capturedBatch = null;

            var resultAck = await _worker.SynchronizeBatchAsync(
                "PC-001",
                "WS-001",
                (req, ct) =>
                {
                    capturedBatch = req;
                    return Task.FromResult<OfflineBatchAcknowledgment?>(new OfflineBatchAcknowledgment
                    {
                        BatchId = req.BatchId,
                        Success = true,
                        ProcessedCount = 2,
                        AcknowledgedEventIds = new List<string> { eventId1, eventId2 }
                    });
                });

            Assert.NotNull(resultAck);
            Assert.NotNull(capturedBatch);
            Assert.Equal("PC-001", capturedBatch.ClientId);
            Assert.Equal("WS-001", capturedBatch.WorkstationId);
            Assert.Equal(2, capturedBatch.Items.Count);
            Assert.Equal(eventId1, capturedBatch.Items[0].EventId);
            Assert.Equal(eventId2, capturedBatch.Items[1].EventId);

            _mockQueue.Verify(q => q.AcknowledgeItemsAsync(It.Is<IEnumerable<string>>(ids => ids.Contains(eventId1) && ids.Contains(eventId2)), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SynchronizeBatch_PartialACK_AcknowledgesAndMarksFailedCorrectly()
        {
            var eventId1 = Guid.NewGuid().ToString("D");
            var eventId2 = Guid.NewGuid().ToString("D");

            var items = new List<DurableQueueItemEntity>
            {
                new DurableQueueItemEntity { EventId = eventId1, EventType = "SESSION_COMMAND_REQUEST", SequenceNumber = 1, ReliabilityClass = "CRITICAL", Payload = "{}" },
                new DurableQueueItemEntity { EventId = eventId2, EventType = "MALFORMED_EVENT", SequenceNumber = 2, ReliabilityClass = "NORMAL", Payload = "{}" }
            };

            _mockQueue.Setup(q => q.ClaimBatchAsync(10, It.IsAny<CancellationToken>()))
                      .ReturnsAsync(items);

            var ack = await _worker.SynchronizeBatchAsync(
                "PC-001",
                "WS-001",
                (req, ct) => Task.FromResult<OfflineBatchAcknowledgment?>(new OfflineBatchAcknowledgment
                {
                    BatchId = req.BatchId,
                    Success = true,
                    ProcessedCount = 1,
                    AcknowledgedEventIds = new List<string> { eventId1 },
                    RejectedEventIds = new List<string> { eventId2 },
                    ErrorMessage = "Invalid event structure"
                }));

            Assert.NotNull(ack);
            _mockQueue.Verify(q => q.AcknowledgeItemsAsync(It.Is<IEnumerable<string>>(ids => ids.Single() == eventId1), It.IsAny<CancellationToken>()), Times.Once);
            _mockQueue.Verify(q => q.MarkFailedAsync(eventId2, "Invalid event structure", true, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SynchronizeBatch_TransportException_ReturnsNullWithoutMutatingQueueACK()
        {
            var eventId = Guid.NewGuid().ToString("D");
            _mockQueue.Setup(q => q.ClaimBatchAsync(10, It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new List<DurableQueueItemEntity> { new DurableQueueItemEntity { EventId = eventId, EventType = "TEST", Payload = "{}" } });

            var ack = await _worker.SynchronizeBatchAsync(
                "PC-001",
                "WS-001",
                (req, ct) => throw new System.IO.IOException("Socket reset during transmission"));

            Assert.Null(ack);
            _mockQueue.Verify(q => q.AcknowledgeItemsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Never);
            _mockQueue.Verify(q => q.MarkFailedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
