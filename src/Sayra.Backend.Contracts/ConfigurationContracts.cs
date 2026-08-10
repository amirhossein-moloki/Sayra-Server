using System;

namespace Sayra.Backend.Contracts
{
    public class ConfigurationPackageContract
    {
        public string Version { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string IssuedBy { get; set; } = string.Empty;
        public string Hash { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
        public object Payload { get; set; } = null!;
        public string PayloadType { get; set; } = string.Empty; // "Full" or "Delta"
        public string? TargetClient { get; set; }
        public string? TargetGroup { get; set; }
    }

    public class ConfigurationDelta
    {
        public string Path { get; set; } = string.Empty;
        public string Op { get; set; } = string.Empty; // "add", "replace", "remove"
        public object? Value { get; set; }
    }
}
