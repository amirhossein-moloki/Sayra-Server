namespace Sayra.Backend.Application.OfflineQueue
{
    public class QueueMetricsDto
    {
        public int TotalCount { get; set; }
        public int PendingCount { get; set; }
        public int InFlightCount { get; set; }
        public int AcknowledgedCount { get; set; }
        public int FailedCount { get; set; }
        public int ExpiredCount { get; set; }
        public long TotalSizeBytes { get; set; }
        public int CriticalCount { get; set; }
        public int ImportantCount { get; set; }
        public int NormalCount { get; set; }
        public double? OldestPendingAgeSeconds { get; set; }
    }
}
