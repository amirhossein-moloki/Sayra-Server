namespace Sayra.Backend.Application.OfflineQueue
{
    public class OfflineQueueOptions
    {
        public const string SectionName = "OfflineQueue";

        public string DbPath { get; set; } = "sayra_offline_queue.db";
        public string? ConnectionString { get; set; }
        public int MaxItemCount { get; set; } = 10000;
        public long MaxStorageSizeBytes { get; set; } = 52428800; // 50 MB
        public int MaxPayloadSizeBytes { get; set; } = 262144; // 256 KB
        public int CriticalRetentionDays { get; set; } = 30;
        public int ImportantRetentionDays { get; set; } = 7;
        public int NormalRetentionHours { get; set; } = 24;
        public string? EncryptionKey { get; set; }
    }
}
