namespace Sayra.Backend.Infrastructure.Configuration.Options
{
    public class UpdatesOptions
    {
        public const string SectionName = "Updates";

        public string LocalUpdateRepositoryPath { get; set; } = "updates/packages/";
        public string TempRepositoryPath { get; set; } = "updates/temp/";
        public long MaxArtifactSizeBytes { get; set; } = 524_288_000; // 500 MB default
        public bool EnableAutoUpdate { get; set; } = false;
        public string UpdatePublicKeyPem { get; set; } = string.Empty;
    }
}
