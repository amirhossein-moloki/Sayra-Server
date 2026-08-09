namespace Sayra.Backend.Infrastructure.Configuration.Options
{
    public class SecurityOptions
    {
        public const string SectionName = "Security";

        public string TokenSigningKey { get; set; } = string.Empty;
        public int TokenLifetimeMinutes { get; set; } = 60;
        public string PrivateKeyPem { get; set; } = string.Empty;
        public string PublicKeyPem { get; set; } = string.Empty;
    }
}
