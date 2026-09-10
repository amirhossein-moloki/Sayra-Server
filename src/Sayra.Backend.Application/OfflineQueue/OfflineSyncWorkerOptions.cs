namespace Sayra.Backend.Application.OfflineQueue
{
    public class OfflineSyncWorkerOptions
    {
        public int MaxBatchSize { get; set; } = 100;
        public int SyncIntervalMs { get; set; } = 5000;
        public int BatchTimeoutSeconds { get; set; } = 30;
        public bool AutoStart { get; set; } = true;
    }
}
