using System;

namespace Sayra.Backend.Application.OfflineQueue
{
    public class OfflineRetryOptions
    {
        public const string SectionName = "OfflineRetry";

        public int MaxRetryCount { get; set; } = 5;
        public double InitialBackoffSeconds { get; set; } = 2.0;
        public double MaxBackoffSeconds { get; set; } = 300.0; // 5 minutes
        public double BackoffMultiplier { get; set; } = 2.0;
        public double JitterFactor { get; set; } = 0.2;
    }
}
