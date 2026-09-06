using System;
using System.Text;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Exceptions;

#nullable enable

namespace Sayra.Backend.Application.Updates
{
    public static class UpdateSigningCanonicalizer
    {
        public static string BuildCanonicalPayloadString(UpdatePackage package)
        {
            if (package == null)
            {
                throw new ArgumentNullException(nameof(package));
            }

            if (package.Id == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_PACKAGE_ID", "Package ID cannot be empty.");
            }

            if (package.ReleaseId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_RELEASE_ID", "Release ID cannot be empty.");
            }

            if (string.IsNullOrWhiteSpace(package.FileName))
            {
                throw new InvalidDomainException("INVALID_FILE_NAME", "Package file name cannot be empty.");
            }

            if (package.Size <= 0)
            {
                throw new InvalidDomainException("INVALID_PACKAGE_SIZE", "Package size must be greater than zero.");
            }

            if (string.IsNullOrWhiteSpace(package.SHA256))
            {
                throw new InvalidDomainException("HASH_MISSING", "Package SHA-256 hash cannot be empty for canonicalization.");
            }

            return $"{package.Id}:{package.ReleaseId}:{package.FileName.Trim()}:{package.Size}:{package.SHA256.Trim().ToLowerInvariant()}";
        }

        public static byte[] BuildCanonicalPayloadBytes(UpdatePackage package)
        {
            string payload = BuildCanonicalPayloadString(package);
            return Encoding.UTF8.GetBytes(payload);
        }

        public static string BuildCanonicalPayloadString(Guid packageId, Guid releaseId, string fileName, long size, string checksumSha256)
        {
            if (packageId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_PACKAGE_ID", "Package ID cannot be empty.");
            }

            if (releaseId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_RELEASE_ID", "Release ID cannot be empty.");
            }

            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new InvalidDomainException("INVALID_FILE_NAME", "Package file name cannot be empty.");
            }

            if (size <= 0)
            {
                throw new InvalidDomainException("INVALID_PACKAGE_SIZE", "Package size must be greater than zero.");
            }

            if (string.IsNullOrWhiteSpace(checksumSha256))
            {
                throw new InvalidDomainException("HASH_MISSING", "Checksum SHA-256 cannot be empty for canonicalization.");
            }

            return $"{packageId}:{releaseId}:{fileName.Trim()}:{size}:{checksumSha256.Trim().ToLowerInvariant()}";
        }

        public static byte[] BuildCanonicalPayloadBytes(Guid packageId, Guid releaseId, string fileName, long size, string checksumSha256)
        {
            string payload = BuildCanonicalPayloadString(packageId, releaseId, fileName, size, checksumSha256);
            return Encoding.UTF8.GetBytes(payload);
        }
    }
}
