namespace Sayra.Backend.Application.Configuration.Models
{
    public class ConfigurationCacheOptions
    {
        public const string SectionName = "ConfigurationCache";

        public bool Enabled { get; set; } = true;
        public int EffectiveConfigurationTtlMinutes { get; set; } = 60;
        public int PublicationMetadataTtlMinutes { get; set; } = 60;
        public int LockTimeoutSeconds { get; set; } = 5;
        public int LockWaitTimeoutMs { get; set; } = 2000;
        public string KeyPrefix { get; set; } = "sayra:config:v1:";
    }
}
