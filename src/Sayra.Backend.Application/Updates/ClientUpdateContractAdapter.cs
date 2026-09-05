using System;
using System.Linq;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Exceptions;

#nullable enable

namespace Sayra.Backend.Application.Updates
{
    public static class ClientUpdateContractAdapter
    {
        public static ClientUpdateManifestContract ToManifestContract(
            UpdateRelease release,
            UpdatePackage? package = null,
            string? downloadBaseUrl = null)
        {
            if (release == null)
            {
                throw new ArgumentNullException(nameof(release));
            }

            var selectedPackage = package ?? release.Packages.FirstOrDefault();
            if (selectedPackage == null)
            {
                throw new InvalidDomainException("NO_PACKAGE_AVAILABLE", $"Release '{release.Version}' does not have any package attached.");
            }

            string relativeDownloadUrl = string.Format(ClientUpdateProtocolConstants.DownloadRoutePattern, selectedPackage.Id);
            string packageUrl = string.IsNullOrWhiteSpace(downloadBaseUrl)
                ? relativeDownloadUrl
                : $"{downloadBaseUrl.TrimEnd('/')}{relativeDownloadUrl}";

            bool isMandatory = release.ReleaseType is UpdateReleaseType.Security or UpdateReleaseType.Emergency;

            return new ClientUpdateManifestContract
            {
                Version = release.Version,
                ReleaseNotes = release.ReleaseNotes,
                PackageUrl = packageUrl,
                Checksum = selectedPackage.SHA256 ?? string.Empty,
                Signature = selectedPackage.Signature,
                IsMandatory = isMandatory,
                MinimumSupportedVersion = null,
                FileSize = selectedPackage.Size,
                PackageType = selectedPackage.PackageType.ToString().ToLowerInvariant()
            };
        }

        public static ClientUpdatePackageMetadataContract ToPackageMetadataContract(UpdatePackage package)
        {
            if (package == null)
            {
                throw new ArgumentNullException(nameof(package));
            }

            return new ClientUpdatePackageMetadataContract
            {
                PackageId = package.Id,
                ReleaseId = package.ReleaseId,
                FileName = package.FileName,
                Size = package.Size,
                ChecksumSha256 = package.SHA256 ?? string.Empty,
                Signature = package.Signature,
                SigningKeyId = package.SigningKeyId,
                PackageType = package.PackageType.ToString().ToLowerInvariant(),
                StorageKey = package.StorageKey
            };
        }

        public static UpdateManifest ToLegacyManifest(ClientUpdateManifestContract manifestContract)
        {
            if (manifestContract == null)
            {
                throw new ArgumentNullException(nameof(manifestContract));
            }

            return new UpdateManifest
            {
                Version = manifestContract.Version,
                ReleaseNotes = manifestContract.ReleaseNotes ?? string.Empty,
                PackageUrl = manifestContract.PackageUrl,
                Checksum = manifestContract.Checksum,
                Signature = manifestContract.Signature ?? string.Empty
            };
        }
    }
}
