using System;
using Microsoft.Extensions.Options;

namespace Sayra.Backend.Application.OfflineQueue
{
    public class OfflineRetryPolicyCalculator
    {
        private readonly OfflineRetryOptions _options;
        private readonly Random _random = new();

        public OfflineRetryPolicyCalculator(IOptions<OfflineRetryOptions> options)
        {
            _options = options?.Value ?? new OfflineRetryOptions();
        }

        public OfflineRetryPolicyCalculator(OfflineRetryOptions options)
        {
            _options = options ?? new OfflineRetryOptions();
        }

        public TimeSpan CalculateNextAttemptDelay(int currentRetryCount)
        {
            if (currentRetryCount <= 0) currentRetryCount = 1;

            double baseBackoff = _options.InitialBackoffSeconds * Math.Pow(_options.BackoffMultiplier, currentRetryCount - 1);
            double boundedBackoff = Math.Min(baseBackoff, _options.MaxBackoffSeconds);

            if (_options.JitterFactor > 0)
            {
                double jitterRange = boundedBackoff * _options.JitterFactor;
                double jitter = (_random.NextDouble() * jitterRange) - (jitterRange / 2.0);
                boundedBackoff = Math.Max(0.5, Math.Min(_options.MaxBackoffSeconds, boundedBackoff + jitter));
            }

            return TimeSpan.FromSeconds(boundedBackoff);
        }

        public DateTime CalculateNextAttemptTimestamp(int currentRetryCount, DateTime? baseTime = null)
        {
            var origin = baseTime ?? DateTime.UtcNow;
            return origin.Add(CalculateNextAttemptDelay(currentRetryCount));
        }

        public bool IsRetryExhausted(int currentRetryCount)
        {
            return currentRetryCount >= _options.MaxRetryCount;
        }

        public int MaxRetryCount => _options.MaxRetryCount;
    }
}
