using System;

#nullable enable

namespace Sayra.Backend.Contracts
{
    public class UpdateManifest
    {
        public string Version { get; set; } = string.Empty;
        public string ReleaseNotes { get; set; } = string.Empty;
        public string PackageUrl { get; set; } = string.Empty;
        public string Checksum { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
    }

    public class ClientUpdateManifestContract
    {
        public string Version { get; set; } = string.Empty;
        public string? ReleaseNotes { get; set; }
        public string PackageUrl { get; set; } = string.Empty;
        public string Checksum { get; set; } = string.Empty;
        public string? Signature { get; set; }
        public bool IsMandatory { get; set; }
        public string? MinimumSupportedVersion { get; set; }
        public long FileSize { get; set; }
        public string PackageType { get; set; } = ClientUpdateProtocolConstants.DefaultPackageType;
    }

    public class ClientUpdatePackageMetadataContract
    {
        public Guid PackageId { get; set; }
        public Guid ReleaseId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public long Size { get; set; }
        public string ChecksumSha256 { get; set; } = string.Empty;
        public string? Signature { get; set; }
        public string? SigningKeyId { get; set; }
        public string PackageType { get; set; } = ClientUpdateProtocolConstants.DefaultPackageType;
        public string StorageKey { get; set; } = string.Empty;
    }

    public static class ClientUpdateProtocolConstants
    {
        public const string ManifestRoute = "/api/updates/manifest";
        public const string DownloadRoutePattern = "/api/updates/download/{0}";
        public const string DefaultPackageType = "spk";
        public const string HashAlgorithm = "SHA-256";
        public const string SignatureAlgorithm = "RSA-SHA256";
        public const string WorkstationAuthHeader = "Authorization";
    }
}
