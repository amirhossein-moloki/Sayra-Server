using System;

namespace Sayra.Backend.Domain
{
    public class ConfigurationPackage : BaseEntity
    {
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Content { get; set; } = "{}"; // JSON config content
        public bool IsActive { get; set; }

        // Concurrency token
        public uint RowVersion { get; set; }
    }
}
