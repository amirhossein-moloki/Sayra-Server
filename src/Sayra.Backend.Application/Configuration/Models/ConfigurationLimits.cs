namespace Sayra.Backend.Application.Configuration.Models
{
    public static class ConfigurationLimits
    {
        public const int MaxPayloadSizeBytes = 100 * 1024; // 100 KB
        public const int MaxNestingDepth = 16;
        public const int MaxStringLength = 1024;
        public const int MaxCollectionSize = 100;
        public const bool RejectUnknownProperties = true;
    }

    public enum UnknownPropertyPolicy
    {
        Reject,
        Ignore
    }
}
