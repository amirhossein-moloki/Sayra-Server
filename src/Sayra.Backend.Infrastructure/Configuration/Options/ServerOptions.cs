namespace Sayra.Backend.Infrastructure.Configuration.Options
{
    public class ServerOptions
    {
        public const string SectionName = "Server";

        public int Port { get; set; } = 5000;
        public string Environment { get; set; } = "Production";
        public string Host { get; set; } = "*";
        public int MaxMessageSizeBytes { get; set; } = 65536; // 64 KB production-safe default
    }
}
