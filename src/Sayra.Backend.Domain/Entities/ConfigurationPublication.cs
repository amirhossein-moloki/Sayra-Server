using System;
using Sayra.Backend.Domain.Exceptions;

namespace Sayra.Backend.Domain.Entities
{
    public class ConfigurationPublication : BaseEntity
    {
        public Guid ConfigurationPackageId { get; set; }
        public Guid? ConfigurationTargetId { get; set; }
        public ConfigurationStatus Status { get; set; } = ConfigurationStatus.PUBLISHED;
        public DateTime PublishedAt { get; set; } = DateTime.UtcNow;
        public string PublishedBy { get; set; } = string.Empty;
        public string? Notes { get; set; }

        // Concurrency token
        public uint RowVersion { get; set; }

        // Navigation properties
        public ConfigurationPackage? Package { get; set; }
        public ConfigurationTarget? Target { get; set; }

        public static ConfigurationPublication Create(
            Guid configurationPackageId,
            string publishedBy,
            Guid? configurationTargetId = null,
            string? notes = null)
        {
            if (configurationPackageId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_PUBLICATION", "Configuration package ID cannot be empty.");
            }

            if (string.IsNullOrWhiteSpace(publishedBy))
            {
                throw new InvalidDomainException("INVALID_PUBLICATION", "PublishedBy identity is required.");
            }

            return new ConfigurationPublication
            {
                ConfigurationPackageId = configurationPackageId,
                ConfigurationTargetId = configurationTargetId,
                PublishedBy = publishedBy.Trim(),
                PublishedAt = DateTime.UtcNow,
                Status = ConfigurationStatus.PUBLISHED,
                Notes = notes
            };
        }
    }
}
