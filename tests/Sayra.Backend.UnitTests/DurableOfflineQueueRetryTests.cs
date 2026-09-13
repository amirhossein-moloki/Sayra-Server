using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sayra.Backend.Application.OfflineQueue;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Infrastructure.OfflineQueue;
using Sayra.Backend.Infrastructure.Persistence;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class DurableOfflineQueueRetryTests
    {
        private SqliteOfflineQueueDbContext CreateTestDbContext(string dbName)
        {
            var options = new DbContextOptionsBuilder<SqliteOfflineQueueDbContext>()
                .UseSqlite($"Data Source={dbName}")
                .Options;

            var context = new SqliteOfflineQueueDbContext(options);
            context.Database.EnsureCreated();
            return context;
        }

        [Fact]
        public async Task ScheduleRetryOrDeadLetterAsync_TransientFailure_SchedulesNextAttemptWithBackoff()
        {
            string dbName = $"test_retry_{Guid.NewGuid()}.db";
            try
            {
                using var context = CreateTestDbContext(dbName);
                var queue = new SqliteDurableOfflineQueue(context, Options.Create(new OfflineQueueOptions()), NullLogger<SqliteDurableOfflineQueue>.Instance);

                var env = new ClientEventEnvelopeDto
                {
                    EventId = Guid.NewGuid().ToString("D"),
                    EventType = ClientEventType.SecurityEvent,
                    Payload = "{}"
                };

                await queue.EnqueueAsync(env);

                // Schedule transient failure with 10s backoff
                await queue.ScheduleRetryOrDeadLetterAsync(env.EventId, "Transient network timeout", isPermanent: false, backoffDelay: TimeSpan.FromSeconds(10));

                var item = await context.QueueItems.FirstAsync(x => x.EventId == env.EventId);
                Assert.Equal(1, item.RetryCount);
                Assert.Equal(OfflineQueueItemStatus.Pending, item.Status);
                Assert.NotNull(item.NextAttemptAt);
                Assert.True(item.NextAttemptAt > DateTime.UtcNow.AddSeconds(5));

                // ClaimBatch should NOT claim this item because NextAttemptAt is in future!
                var claimedNow = await queue.ClaimBatchAsync(10);
                Assert.Empty(claimedNow);
            }
            finally
            {
                if (File.Exists(dbName)) File.Delete(dbName);
            }
        }

        [Fact]
        public async Task ScheduleRetryOrDeadLetterAsync_PermanentFailure_TransitionsToFailedStatus()
        {
            string dbName = $"test_retry_{Guid.NewGuid()}.db";
            try
            {
                using var context = CreateTestDbContext(dbName);
                var queue = new SqliteDurableOfflineQueue(context, Options.Create(new OfflineQueueOptions()), NullLogger<SqliteDurableOfflineQueue>.Instance);

                var env = new ClientEventEnvelopeDto
                {
                    EventId = Guid.NewGuid().ToString("D"),
                    EventType = ClientEventType.SecurityEvent,
                    Payload = "{}"
                };

                await queue.EnqueueAsync(env);

                await queue.ScheduleRetryOrDeadLetterAsync(env.EventId, "Security identity mismatch", isPermanent: true);

                var item = await context.QueueItems.FirstAsync(x => x.EventId == env.EventId);
                Assert.Equal(1, item.RetryCount);
                Assert.Equal(OfflineQueueItemStatus.Failed, item.Status);
                Assert.NotNull(item.ExpiresAt);
            }
            finally
            {
                if (File.Exists(dbName)) File.Delete(dbName);
            }
        }

        [Fact]
        public async Task ScheduleRetryOrDeadLetterAsync_RetryExhausted_TransitionsToFailedStatus()
        {
            string dbName = $"test_retry_{Guid.NewGuid()}.db";
            try
            {
                using var context = CreateTestDbContext(dbName);
                var retryOptions = new OfflineRetryOptions { MaxRetryCount = 3 };
                var calculator = new OfflineRetryPolicyCalculator(retryOptions);
                var queue = new SqliteDurableOfflineQueue(context, Options.Create(new OfflineQueueOptions()), NullLogger<SqliteDurableOfflineQueue>.Instance, calculator);

                var env = new ClientEventEnvelopeDto
                {
                    EventId = Guid.NewGuid().ToString("D"),
                    EventType = ClientEventType.SecurityEvent,
                    Payload = "{}"
                };

                await queue.EnqueueAsync(env);

                // Attempt 1, 2 (transient)
                await queue.ScheduleRetryOrDeadLetterAsync(env.EventId, "Fail 1", isPermanent: false, backoffDelay: TimeSpan.FromSeconds(0));
                await queue.ScheduleRetryOrDeadLetterAsync(env.EventId, "Fail 2", isPermanent: false, backoffDelay: TimeSpan.FromSeconds(0));

                var item = await context.QueueItems.FirstAsync(x => x.EventId == env.EventId);
                Assert.Equal(2, item.RetryCount);
                Assert.Equal(OfflineQueueItemStatus.Pending, item.Status);

                // Attempt 3 (reaches max retry count 3)
                await queue.ScheduleRetryOrDeadLetterAsync(env.EventId, "Fail 3", isPermanent: false, backoffDelay: TimeSpan.FromSeconds(0));

                item = await context.QueueItems.FirstAsync(x => x.EventId == env.EventId);
                Assert.Equal(3, item.RetryCount);
                Assert.Equal(OfflineQueueItemStatus.Failed, item.Status);
            }
            finally
            {
                if (File.Exists(dbName)) File.Delete(dbName);
            }
        }
    }
}
