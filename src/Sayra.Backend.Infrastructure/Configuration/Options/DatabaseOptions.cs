namespace Sayra.Backend.Infrastructure.Configuration.Options
{
    public class DatabaseOptions
    {
        public const string SectionName = "Database";

        public string ConnectionString { get; set; } = string.Empty;
        public int MaxPoolSize { get; set; } = 100;
        public bool EnableSensitiveDataLogging { get; set; } = false;
    }
}
