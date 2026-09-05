using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Sayra.Backend.Domain.Exceptions;

#nullable enable

namespace Sayra.Backend.Domain
{
    public class UpdateRelease : BaseEntity
    {
        private static readonly Regex SafeVersionRegex = new(@"^[a-zA-Z0-9\.\+\-_]{1,64}$", RegexOptions.Compiled);

        public Guid OrganizationId { get; private set; }
        public string Version { get; private set; } = string.Empty;
        public UpdateReleaseType ReleaseType { get; private set; }
        public UpdateReleaseStatus Status { get; private set; }
        public string? ReleaseNotes { get; private set; }
        public string CreatedBy { get; private set; } = "system";
        public DateTime? PublishedAt { get; private set; }
        public DateTime? RevokedAt { get; private set; }
        public DateTime? SupersededAt { get; private set; }
        public string? Metadata { get; private set; }

        private readonly List<UpdatePackage> _packages = new();
        public IReadOnlyCollection<UpdatePackage> Packages => _packages.AsReadOnly();

        // Concurrency token
        public uint RowVersion { get; set; }

        // EF Core requirement
        public UpdateRelease()
        {
        }

        public static UpdateRelease Create(
            Guid organizationId,
            string version,
            UpdateReleaseType releaseType = UpdateReleaseType.Standard,
            string? releaseNotes = null,
            string createdBy = "system",
            string? metadata = null)
        {
            if (organizationId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_ORGANIZATION", "Organization ID must not be empty.");
            }

            var normalizedVersion = NormalizeAndValidateVersion(version);

            if (!string.IsNullOrWhiteSpace(metadata) && metadata.Length > 4096)
            {
                throw new InvalidDomainException("METADATA_TOO_LARGE", "Metadata payload exceeds maximum allowed length of 4096 characters.");
            }

            if (!string.IsNullOrWhiteSpace(releaseNotes) && releaseNotes.Length > 2048)
            {
                throw new InvalidDomainException("RELEASE_NOTES_TOO_LARGE", "Release notes exceed maximum allowed length of 2048 characters.");
            }

            return new UpdateRelease
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                Version = normalizedVersion,
                ReleaseType = releaseType,
                Status = UpdateReleaseStatus.Draft,
                ReleaseNotes = string.IsNullOrWhiteSpace(releaseNotes) ? null : releaseNotes.Trim(),
                CreatedBy = string.IsNullOrWhiteSpace(createdBy) ? "system" : createdBy.Trim(),
                CreatedAt = DateTime.UtcNow,
                Metadata = string.IsNullOrWhiteSpace(metadata) ? null : metadata.Trim()
            };
        }

        public static string NormalizeAndValidateVersion(string rawVersion)
        {
            if (string.IsNullOrWhiteSpace(rawVersion))
            {
                throw new InvalidDomainException("INVALID_VERSION", "Release version cannot be null or empty.");
            }

            var trimmed = rawVersion.Trim();

            if (!SafeVersionRegex.IsMatch(trimmed))
            {
                throw new InvalidDomainException("INVALID_VERSION_FORMAT", $"Release version '{trimmed}' contains invalid characters or exceeds safe length limits.");
            }

            return trimmed;
        }

        public void TransitionTo(UpdateReleaseStatus newStatus)
        {
            UpdateReleaseStatusValidator.ValidateTransition(Status, newStatus);

            Status = newStatus;
            UpdatedAt = DateTime.UtcNow;

            if (newStatus == UpdateReleaseStatus.Published && PublishedAt == null)
            {
                PublishedAt = DateTime.UtcNow;
            }
            else if (newStatus == UpdateReleaseStatus.Active && PublishedAt == null)
            {
                PublishedAt = DateTime.UtcNow;
            }
            else if (newStatus == UpdateReleaseStatus.Superseded && SupersededAt == null)
            {
                SupersededAt = DateTime.UtcNow;
            }
            else if (newStatus == UpdateReleaseStatus.Revoked && RevokedAt == null)
            {
                RevokedAt = DateTime.UtcNow;
            }
        }

        public void UpdateMetadata(string? releaseNotes, string? metadata)
        {
            if (IsImmutableState())
            {
                throw new InvalidDomainException("RELEASE_IMMUTABLE", $"Release '{Version}' in status '{Status}' is immutable and cannot be modified.");
            }

            if (!string.IsNullOrWhiteSpace(metadata) && metadata.Length > 4096)
            {
                throw new InvalidDomainException("METADATA_TOO_LARGE", "Metadata payload exceeds maximum allowed length of 4096 characters.");
            }

            if (!string.IsNullOrWhiteSpace(releaseNotes) && releaseNotes.Length > 2048)
            {
                throw new InvalidDomainException("RELEASE_NOTES_TOO_LARGE", "Release notes exceed maximum allowed length of 2048 characters.");
            }

            ReleaseNotes = string.IsNullOrWhiteSpace(releaseNotes) ? null : releaseNotes.Trim();
            Metadata = string.IsNullOrWhiteSpace(metadata) ? null : metadata.Trim();
            UpdatedAt = DateTime.UtcNow;
        }

        public void AddPackage(UpdatePackage package)
        {
            if (IsImmutableState())
            {
                throw new InvalidDomainException("RELEASE_IMMUTABLE", $"Cannot attach package to release '{Version}' in immutable status '{Status}'.");
            }

            if (package == null)
            {
                throw new ArgumentNullException(nameof(package));
            }

            if (package.ReleaseId != Id)
            {
                throw new InvalidDomainException("PACKAGE_RELEASE_MISMATCH", $"Package release ID '{package.ReleaseId}' does not match release ID '{Id}'.");
            }

            _packages.Add(package);
            UpdatedAt = DateTime.UtcNow;
        }

        public bool IsImmutableState()
        {
            return Status is UpdateReleaseStatus.Published
                        or UpdateReleaseStatus.Active
                        or UpdateReleaseStatus.Superseded
                        or UpdateReleaseStatus.Revoked
                        or UpdateReleaseStatus.Cancelled;
        }
    }
}
