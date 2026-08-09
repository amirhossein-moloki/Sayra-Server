namespace Sayra.Backend.Infrastructure.Configuration.Options
{
    public class DiscoveryOptions
    {
        public const string SectionName = "Discovery";

        public bool Enabled { get; set; } = true;
        public int UdpPort { get; set; } = 37020;
        public string ServerName { get; set; } = "SayraServer";
        public int Priority { get; set; } = 1;
    }
}
