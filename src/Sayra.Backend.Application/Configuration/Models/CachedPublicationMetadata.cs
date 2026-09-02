using System;

namespace Sayra.Backend.Application.Configuration.Models
{
    public class CachedPublicationMetadata
    {
        public int CacheSchemaVersion { get; set; } = 1;
        public Guid PublicationId { get; set; }
        public Guid ConfigurationPackageId { get; set; }
        public long VersionNumber { get; set; }
        public string Version { get; set; } = string.Empty;
        public Guid ConfigurationTargetId { get; set; }
        public Guid OrganizationId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string ConfigurationHash { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
        public string SigningKeyId { get; set; } = string.Empty;
        public string SignatureAlgorithm { get; set; } = string.Empty;
        public DateTime CachedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
