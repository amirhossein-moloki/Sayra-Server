using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sayra.Backend.Application.Resilience;
using Sayra.Backend.Infrastructure.Configuration.Options;

#nullable enable

namespace Sayra.Backend.Infrastructure.Resilience
{
    public class ResiliencePipeline : IResiliencePipeline
    {
        private readonly ResilienceOptions _options;
        private readonly IResilienceMetrics? _metrics;
        private readonly ILogger<ResiliencePipeline> _logger;
        private readonly ConcurrentDictionary<string, ICircuitBreaker> _circuitBreakers = new(StringComparer.OrdinalIgnoreCase);
        private readonly Random _random = new();

        public ResiliencePipeline(
            IOptions<ResilienceOptions> options,
            IResilienceMetrics? metrics = null,
            ILogger<ResiliencePipeline>? logger = null)
        {
            _options = options?.Value ?? new ResilienceOptions();
            _metrics = metrics;
            _logger = logger ?? NullLogger<ResiliencePipeline>.Instance;
        }

        public async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            OperationRetrySafety safety,
            string dependencyName,
            string operationName,
            CancellationToken cancellationToken = default)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            string sanitizedDependency = string.IsNullOrWhiteSpace(dependencyName) ? "Default" : dependencyName;
            string sanitizedOperation = string.IsNullOrWhiteSpace(operationName) ? "Execute" : operationName;

            int maxAttempts = safety == OperationRetrySafety.NonRetryableWrite ? 1 : Math.Max(1, _options.MaxRetryAttempts);

            var cb = GetOrCreateCircuitBreaker(sanitizedDependency);
            if (!cb.AllowExecution())
            {
                _metrics?.RecordCircuitBreakerTrip(sanitizedDependency, cb.State.ToString(), cb.State.ToString());
                _logger.LogWarning("Circuit breaker for dependency '{Dependency}' is OPEN. Rejecting '{Operation}'.", sanitizedDependency, sanitizedOperation);
                throw new CircuitBreakerOpenException(sanitizedDependency);
            }

            using var overallCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (_options.OverallTimeoutSeconds > 0)
            {
                overallCts.CancelAfter(TimeSpan.FromSeconds(_options.OverallTimeoutSeconds));
            }

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _metrics?.RecordCancellation(sanitizedDependency, sanitizedOperation);
                    cancellationToken.ThrowIfCancellationRequested();
                }

                if (overallCts.IsCancellationRequested)
                {
                    _metrics?.RecordTimeout(sanitizedDependency, sanitizedOperation, isOverallTimeout: true);
                    throw new TimeoutException($"Overall timeout of {_options.OverallTimeoutSeconds}s exceeded for {sanitizedDependency}:{sanitizedOperation}.");
                }

                using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(overallCts.Token);
                if (_options.AttemptTimeoutSeconds > 0)
                {
                    attemptCts.CancelAfter(TimeSpan.FromSeconds(_options.AttemptTimeoutSeconds));
                }

                try
                {
                    T result = await operation(attemptCts.Token);
                    cb.RecordSuccess();
                    return result;
                }
                catch (OperationCanceledException ex)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        _metrics?.RecordCancellation(sanitizedDependency, sanitizedOperation);
                        throw;
                    }

                    if (overallCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                    {
                        _metrics?.RecordTimeout(sanitizedDependency, sanitizedOperation, isOverallTimeout: true);
                        _logger.LogWarning("Operation '{Operation}' on dependency '{Dependency}' exceeded overall timeout of {Timeout}s.",
                            sanitizedOperation, sanitizedDependency, _options.OverallTimeoutSeconds);
                        throw new TimeoutException($"Overall timeout of {_options.OverallTimeoutSeconds}s exceeded for {sanitizedDependency}:{sanitizedOperation}.", ex);
                    }

                    // Attempt timeout
                    _metrics?.RecordTimeout(sanitizedDependency, sanitizedOperation, isOverallTimeout: false);
                    cb.RecordFailure(ex);

                    if (attempt >= maxAttempts)
                    {
                        _metrics?.RecordRetryExhausted(sanitizedDependency, sanitizedOperation, attempt);
                        throw new TimeoutException($"Attempt timeout of {_options.AttemptTimeoutSeconds}s exceeded for {sanitizedDependency}:{sanitizedOperation}.", ex);
                    }

                    TimeSpan attemptBackoff = CalculateBackoffWithJitter(attempt);
                    _logger.LogWarning("Attempt {Attempt}/{MaxAttempts} timed out for {Dependency}:{Operation}. Retrying in {DelayMs:F0}ms...",
                        attempt, maxAttempts, sanitizedDependency, sanitizedOperation, attemptBackoff.TotalMilliseconds);

                    try
                    {
                        await Task.Delay(attemptBackoff, overallCts.Token);
                    }
                    catch (OperationCanceledException delayEx)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            _metrics?.RecordCancellation(sanitizedDependency, sanitizedOperation);
                            throw;
                        }

                        if (overallCts.IsCancellationRequested)
                        {
                            _metrics?.RecordTimeout(sanitizedDependency, sanitizedOperation, isOverallTimeout: true);
                            throw new TimeoutException($"Overall timeout of {_options.OverallTimeoutSeconds}s exceeded for {sanitizedDependency}:{sanitizedOperation}.", delayEx);
                        }
                    }
                }
                catch (Exception ex)
                {
                    var failureCategory = ClassifyException(ex);
                    cb.RecordFailure(ex);

                    if (failureCategory == ResilienceFailureCategory.NonRetryable ||
                        failureCategory == ResilienceFailureCategory.FailFast ||
                        safety == OperationRetrySafety.NonRetryableWrite ||
                        attempt >= maxAttempts)
                    {
                        if (attempt >= maxAttempts && failureCategory == ResilienceFailureCategory.Retryable)
                        {
                            _metrics?.RecordRetryExhausted(sanitizedDependency, sanitizedOperation, attempt);
                        }
                        throw;
                    }

                    TimeSpan backoff = CalculateBackoffWithJitter(attempt);
                    _metrics?.RecordRetryAttempt(sanitizedDependency, sanitizedOperation, attempt, failureCategory.ToString());
                    _logger.LogWarning(ex, "Attempt {Attempt}/{MaxAttempts} failed for {Dependency}:{Operation} ({Category}). Retrying in {DelayMs:F0}ms...",
                        attempt, maxAttempts, sanitizedDependency, sanitizedOperation, failureCategory, backoff.TotalMilliseconds);

                    try
                    {
                        await Task.Delay(backoff, overallCts.Token);
                    }
                    catch (OperationCanceledException delayEx)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            _metrics?.RecordCancellation(sanitizedDependency, sanitizedOperation);
                            throw;
                        }

                        if (overallCts.IsCancellationRequested)
                        {
                            _metrics?.RecordTimeout(sanitizedDependency, sanitizedOperation, isOverallTimeout: true);
                            throw new TimeoutException($"Overall timeout of {_options.OverallTimeoutSeconds}s exceeded for {sanitizedDependency}:{sanitizedOperation}.", delayEx);
                        }
                    }
                }
            }

            throw new InvalidOperationException($"Unexpected retry pipeline completion for {sanitizedDependency}:{sanitizedOperation}.");
        }

        public async Task ExecuteAsync(
            Func<CancellationToken, Task> operation,
            OperationRetrySafety safety,
            string dependencyName,
            string operationName,
            CancellationToken cancellationToken = default)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));

            await ExecuteAsync<bool>(async token =>
            {
                await operation(token);
                return true;
            }, safety, dependencyName, operationName, cancellationToken);
        }

        public ResilienceFailureCategory ClassifyException(Exception exception)
        {
            if (exception == null) return ResilienceFailureCategory.NonRetryable;

            if (exception is CircuitBreakerOpenException)
                return ResilienceFailureCategory.FailFast;

            if (exception is TimeoutException or SocketException or IOException)
                return ResilienceFailureCategory.Retryable;

            if (exception is OperationCanceledException)
                return ResilienceFailureCategory.NonRetryable;

            string name = exception.GetType().Name;

            if (name.Contains("NpgsqlException") || name.Contains("DbException") || name.Contains("RedisConnectionException") || name.Contains("RedisTimeoutException"))
                return ResilienceFailureCategory.Retryable;

            if (name.Contains("DbUpdateConcurrencyException") || name.Contains("ConcurrencyException"))
                return ResilienceFailureCategory.Retryable;

            if (exception is ArgumentException or InvalidOperationException or UnauthorizedAccessException)
                return ResilienceFailureCategory.NonRetryable;

            return ResilienceFailureCategory.NonRetryable;
        }

        private ICircuitBreaker GetOrCreateCircuitBreaker(string dependencyName)
        {
            return _circuitBreakers.GetOrAdd(dependencyName, name =>
                new CircuitBreaker(name, _options.CircuitBreakerFailureThreshold, TimeSpan.FromSeconds(_options.CircuitBreakerBreakDurationSeconds), _logger));
        }

        private TimeSpan CalculateBackoffWithJitter(int attempt)
        {
            double baseBackoff = _options.InitialBackoffSeconds * Math.Pow(2, attempt - 1);
            double boundedBackoff = Math.Min(baseBackoff, _options.MaxBackoffSeconds);

            if (_options.JitterFactor > 0)
            {
                double jitterRange = boundedBackoff * _options.JitterFactor;
                double jitter;
                lock (_random)
                {
                    jitter = (_random.NextDouble() * jitterRange) - (jitterRange / 2.0);
                }
                boundedBackoff = Math.Max(0.01, Math.Min(_options.MaxBackoffSeconds, boundedBackoff + jitter));
            }

            return TimeSpan.FromSeconds(boundedBackoff);
        }
    }
}
