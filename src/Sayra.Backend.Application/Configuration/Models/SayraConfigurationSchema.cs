using System.Text.Json.Serialization;

namespace Sayra.Backend.Application.Configuration.Models
{
    public class SayraConfigurationSchema
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0";

        [JsonPropertyName("server")]
        public ServerConfigurationSection Server { get; set; } = new ServerConfigurationSection();

        [JsonPropertyName("discovery")]
        public DiscoveryConfigurationSection Discovery { get; set; } = new DiscoveryConfigurationSection();

        [JsonPropertyName("heartbeat")]
        public HeartbeatConfigurationSection Heartbeat { get; set; } = new HeartbeatConfigurationSection();

        [JsonPropertyName("kiosk")]
        public KioskConfigurationSection Kiosk { get; set; } = new KioskConfigurationSection();

        [JsonPropertyName("localization")]
        public LocalizationConfigurationSection Localization { get; set; } = new LocalizationConfigurationSection();

        [JsonPropertyName("security")]
        public SecurityConfigurationSection Security { get; set; } = new SecurityConfigurationSection();
    }

    public class ServerConfigurationSection
    {
        [JsonPropertyName("ipAddress")]
        public string IpAddress { get; set; } = "127.0.0.1";

        [JsonPropertyName("port")]
        public int Port { get; set; } = 5000;
    }

    public class DiscoveryConfigurationSection
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("port")]
        public int Port { get; set; } = 37020;
    }

    public class HeartbeatConfigurationSection
    {
        [JsonPropertyName("intervalSeconds")]
        public int IntervalSeconds { get; set; } = 10;

        [JsonPropertyName("timeoutSeconds")]
        public int TimeoutSeconds { get; set; } = 30;
    }

    public class KioskConfigurationSection
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("allowShellEscape")]
        public bool AllowShellEscape { get; set; } = false;

        [JsonPropertyName("autoLoginGamer")]
        public bool AutoLoginGamer { get; set; } = false;

        [JsonPropertyName("idleTimeoutMinutes")]
        public int IdleTimeoutMinutes { get; set; } = 15;
    }

    public class LocalizationConfigurationSection
    {
        [JsonPropertyName("culture")]
        public string Culture { get; set; } = "en-US";

        [JsonPropertyName("timeZone")]
        public string TimeZone { get; set; } = "UTC";
    }

    public class SecurityConfigurationSection
    {
        [JsonPropertyName("enableSsl")]
        public bool EnableSsl { get; set; } = true;

        [JsonPropertyName("requireEncryption")]
        public bool RequireEncryption { get; set; } = true;

        [JsonPropertyName("maxFailedAttempts")]
        public int MaxFailedAttempts { get; set; } = 5;
    }
}
