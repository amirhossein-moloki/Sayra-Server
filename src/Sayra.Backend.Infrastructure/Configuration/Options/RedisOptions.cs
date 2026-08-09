namespace Sayra.Backend.Infrastructure.Configuration.Options
{
    public class RedisOptions
    {
        public const string SectionName = "Redis";

        public string ConnectionString { get; set; } = string.Empty;
        public string InstanceName { get; set; } = "Sayra:";
        public int DefaultDatabase { get; set; } = 0;
    }
}
