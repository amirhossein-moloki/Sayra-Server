namespace Sayra.Backend.Infrastructure.Configuration.Options
{
    public class UpdatesOptions
    {
        public const string SectionName = "Updates";

        public string LocalUpdateRepositoryPath { get; set; } = "updates/";
        public bool EnableAutoUpdate { get; set; } = false;
        public string UpdatePublicKeyPem { get; set; } = string.Empty;
    }
}
