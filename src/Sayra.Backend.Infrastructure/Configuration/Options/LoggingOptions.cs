namespace Sayra.Backend.Infrastructure.Configuration.Options
{
    public class LoggingOptions
    {
        public const string SectionName = "Logging";

        public string MinimumLevel { get; set; } = "Information";
        public bool WriteToConsole { get; set; } = true;
        public string FilePath { get; set; } = "logs/sayra-backend-.txt";
    }
}
