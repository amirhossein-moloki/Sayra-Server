using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Sayra.Backend.Application.Resilience;
using Sayra.Backend.Infrastructure.Configuration.Options;
using Sayra.Backend.Infrastructure.Diagnostics;
using Sayra.Backend.Infrastructure.Resilience;
using Xunit;

namespace Sayra.Backend.UnitTests.Resilience
{
    public class ResiliencePolicyUnitTests
    {
        private readonly ResilienceOptions _fastOptions = new()
        {
            MaxRetryAttempts = 3,
            InitialBackoffSeconds = 0.01,
            MaxBackoffSeconds = 0.05,
            JitterFactor = 0.1,
            OverallTimeoutSeconds = 10.0,
            AttemptTimeoutSeconds = 2.0,
            CircuitBreakerFailureThreshold = 5,
            CircuitBreakerBreakDurationSeconds = 0.2
        };

        private readonly ResilienceMetrics _metrics = new();

        [Fact]
        public async Task Transient_Failure_Should_Retry_And_Succeed()
        {
            var pipeline = new ResiliencePipeline(Options.Create(_fastOptions), _metrics);
            int callCount = 0;

            string result = await pipeline.ExecuteAsync(async ct =>
            {
                callCount++;
                if (callCount < 3)
                {
                    throw new TimeoutException("Simulated transient socket timeout");
                }
                return await Task.FromResult("SUCCESS");
            }, OperationRetrySafety.SafeRead, "TestDatabase", "GetRecord");

            Assert.Equal(3, callCount);
            Assert.Equal("SUCCESS", result);
        }

        [Fact]
        public async Task Retry_Exhaustion_Should_Throw_Final_Exception()
        {
            var pipeline = new ResiliencePipeline(Options.Create(_fastOptions), _metrics);
            int callCount = 0;

            await Assert.ThrowsAsync<TimeoutException>(async () =>
            {
                await pipeline.ExecuteAsync<string>(async ct =>
                {
                    callCount++;
                    throw new TimeoutException("Persistent timeout failure");
                }, OperationRetrySafety.SafeRead, "TestDatabase", "GetRecord");
            });

            Assert.Equal(3, callCount);
        }

        [Fact]
        public async Task Non_Retryable_Exception_Should_Not_Retry()
        {
            var pipeline = new ResiliencePipeline(Options.Create(_fastOptions), _metrics);
            int callCount = 0;

            await Assert.ThrowsAsync<ArgumentException>(async () =>
            {
                await pipeline.ExecuteAsync<string>(async ct =>
                {
                    callCount++;
                    throw new ArgumentException("Invalid payload");
                }, OperationRetrySafety.SafeRead, "TestDatabase", "GetRecord");
            });

            Assert.Equal(1, callCount);
        }

        [Fact]
        public async Task Non_Retryable_Write_Should_Not_Retry_On_Failure()
        {
            var pipeline = new ResiliencePipeline(Options.Create(_fastOptions), _metrics);
            int callCount = 0;

            await Assert.ThrowsAsync<TimeoutException>(async () =>
            {
                await pipeline.ExecuteAsync<string>(async ct =>
                {
                    callCount++;
                    throw new TimeoutException("Transient error during payment debit");
                }, OperationRetrySafety.NonRetryableWrite, "FinancialLedger", "DebitAccount");
            });

            Assert.Equal(1, callCount);
        }

        [Fact]
        public async Task Cancellation_Token_Should_Interrupt_Retry_Immediately()
        {
            var pipeline = new ResiliencePipeline(Options.Create(_fastOptions), _metrics);
            using var cts = new CancellationTokenSource();
            int callCount = 0;

            var task = pipeline.ExecuteAsync<string>(async ct =>
            {
                callCount++;
                cts.Cancel();
                throw new TimeoutException("Transient error");
            }, OperationRetrySafety.SafeRead, "TestDatabase", "Query", cts.Token);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
            Assert.Equal(1, callCount);
        }

        [Fact]
        public async Task Overall_Timeout_Should_Cancel_Execution()
        {
            var options = new ResilienceOptions
            {
                MaxRetryAttempts = 5,
                InitialBackoffSeconds = 0.5,
                MaxBackoffSeconds = 1.0,
                OverallTimeoutSeconds = 0.2,
                AttemptTimeoutSeconds = 1.0
            };
            var pipeline = new ResiliencePipeline(Options.Create(options), _metrics);

            await Assert.ThrowsAsync<TimeoutException>(async () =>
            {
                await pipeline.ExecuteAsync<string>(async ct =>
                {
                    await Task.Delay(500, ct);
                    return "Done";
                }, OperationRetrySafety.SafeRead, "SlowDependency", "LongRunningQuery");
            });
        }

        [Fact]
        public async Task CircuitBreaker_Should_Transition_Closed_To_Open_To_HalfOpen_To_Closed()
        {
            var cb = new CircuitBreaker("TestDependency", failureThreshold: 2, breakDuration: TimeSpan.FromMilliseconds(150));

            Assert.Equal(CircuitState.Closed, cb.State);
            Assert.True(cb.AllowExecution());

            cb.RecordFailure();
            Assert.Equal(CircuitState.Closed, cb.State);

            cb.RecordFailure();
            Assert.Equal(CircuitState.Open, cb.State);

            Assert.False(cb.AllowExecution());

            await Task.Delay(200);

            Assert.True(cb.AllowExecution());
            Assert.Equal(CircuitState.HalfOpen, cb.State);

            cb.RecordSuccess();
            Assert.Equal(CircuitState.Closed, cb.State);
        }

        [Fact]
        public async Task CircuitBreaker_Open_Should_Fast_Fail_Without_Invoking_Operation()
        {
            var options = new ResilienceOptions
            {
                MaxRetryAttempts = 1,
                CircuitBreakerFailureThreshold = 1,
                CircuitBreakerBreakDurationSeconds = 10.0
            };
            var pipeline = new ResiliencePipeline(Options.Create(options), _metrics);
            int callCount = 0;

            // First call fails, tripping circuit breaker
            await Assert.ThrowsAsync<TimeoutException>(async () =>
            {
                await pipeline.ExecuteAsync<string>(async ct =>
                {
                    callCount++;
                    throw new TimeoutException("Failure 1");
                }, OperationRetrySafety.SafeRead, "FailingService", "Operation");
            });

            Assert.Equal(1, callCount);

            // Second call fast fails via CircuitBreakerOpenException without invoking operation
            await Assert.ThrowsAsync<CircuitBreakerOpenException>(async () =>
            {
                await pipeline.ExecuteAsync<string>(async ct =>
                {
                    callCount++;
                    return "Result";
                }, OperationRetrySafety.SafeRead, "FailingService", "Operation");
            });

            Assert.Equal(1, callCount);
        }

        [Fact]
        public async Task Nested_Retry_Prevention_Enforces_Single_Resilience_Boundary()
        {
            var pipeline = new ResiliencePipeline(Options.Create(_fastOptions), _metrics);
            int innerCalls = 0;

            // Application boundary controls execution, protecting inner non-retryable write
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await pipeline.ExecuteAsync(async ct =>
                {
                    innerCalls++;
                    throw new InvalidOperationException("Business constraint violation");
                }, OperationRetrySafety.NonRetryableWrite, "OrderService", "ProcessOrder");
            });

            Assert.Equal(1, innerCalls);
        }
    }
}
