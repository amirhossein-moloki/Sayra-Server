namespace Sayra.Backend.Infrastructure.Configuration.Options
{
    public class RateLimitingOptions
    {
        public const string SectionName = "RateLimiting";

        public bool Enabled { get; set; } = true;
        public int GlobalPermitLimit { get; set; } = 100;
        public int AuthPermitLimit { get; set; } = 10;
        public int ConfigSyncPermitLimit { get; set; } = 60;
        public int UpdateManifestPermitLimit { get; set; } = 60;
        public int UpdateDownloadPermitLimit { get; set; } = 10;
        public int QueueLimit { get; set; } = 0;
        public int WindowSeconds { get; set; } = 60;
    }
}
