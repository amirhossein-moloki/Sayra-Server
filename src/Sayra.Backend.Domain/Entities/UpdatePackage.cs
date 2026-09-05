using System;
using System.Text.RegularExpressions;
using Sayra.Backend.Domain.Exceptions;

#nullable enable

namespace Sayra.Backend.Domain
{
    public class UpdatePackage : BaseEntity
    {
        private static readonly Regex SafeStorageKeyRegex = new(@"^[a-zA-Z0-9\.\-_/]{1,256}$", RegexOptions.Compiled);
        private static readonly Regex Sha256Regex = new(@"^[a-fA-F0-9]{64}$", RegexOptions.Compiled);

        public Guid ReleaseId { get; private set; }
        public string FileName { get; private set; } = string.Empty;
        public long Size { get; private set; }
        public string? SHA256 { get; private set; }
        public string? Signature { get; private set; }
        public string? SigningKeyId { get; private set; }
        public string StorageProvider { get; private set; } = "local";
        public string StorageKey { get; private set; } = string.Empty;
        public UpdatePackageType PackageType { get; private set; }
        public UpdatePackageLifecycleState LifecycleState { get; private set; }
        public UpdatePackageVerificationStatus VerificationStatus { get; private set; }

        // Navigation reference
        public UpdateRelease? Release { get; private set; }

        // Concurrency token
        public uint RowVersion { get; set; }

        // EF Core requirement
        public UpdatePackage()
        {
        }

        public static UpdatePackage Create(
            Guid releaseId,
            string fileName,
            long size,
            string storageKey,
            UpdatePackageType packageType = UpdatePackageType.Spk,
            string storageProvider = "local")
        {
            if (releaseId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_RELEASE_ID", "Release ID cannot be empty.");
            }

            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new InvalidDomainException("INVALID_FILE_NAME", "File name cannot be null or empty.");
            }

            if (fileName.Length > 256)
            {
                throw new InvalidDomainException("FILE_NAME_TOO_LONG", "File name exceeds maximum length of 256 characters.");
            }

            if (size <= 0)
            {
                throw new InvalidDomainException("INVALID_PACKAGE_SIZE", "Package size must be strictly greater than 0 bytes.");
            }

            var normalizedStorageKey = ValidateAndNormalizeStorageKey(storageKey);

            return new UpdatePackage
            {
                Id = Guid.NewGuid(),
                ReleaseId = releaseId,
                FileName = fileName.Trim(),
                Size = size,
                StorageProvider = string.IsNullOrWhiteSpace(storageProvider) ? "local" : storageProvider.Trim(),
                StorageKey = normalizedStorageKey,
                PackageType = packageType,
                LifecycleState = UpdatePackageLifecycleState.Uploading,
                VerificationStatus = UpdatePackageVerificationStatus.NotVerified,
                CreatedAt = DateTime.UtcNow
            };
        }

        public static string ValidateAndNormalizeStorageKey(string rawStorageKey)
        {
            if (string.IsNullOrWhiteSpace(rawStorageKey))
            {
                throw new InvalidDomainException("INVALID_STORAGE_KEY", "Storage key cannot be null or empty.");
            }

            var trimmed = rawStorageKey.Trim();

            if (!SafeStorageKeyRegex.IsMatch(trimmed) || trimmed.Contains(".."))
            {
                throw new InvalidDomainException("UNSAFE_STORAGE_KEY", $"Storage key '{trimmed}' contains unsafe characters or directory traversal sequences.");
            }

            return trimmed;
        }

        public void TransitionLifecycle(UpdatePackageLifecycleState newState)
        {
            UpdatePackageLifecycleValidator.ValidateTransition(LifecycleState, newState);

            LifecycleState = newState;
            UpdatedAt = DateTime.UtcNow;

            if (newState == UpdatePackageLifecycleState.Validating)
            {
                VerificationStatus = UpdatePackageVerificationStatus.Validating;
            }
            else if (newState == UpdatePackageLifecycleState.Validated)
            {
                VerificationStatus = UpdatePackageVerificationStatus.Valid;
            }
            else if (newState == UpdatePackageLifecycleState.ValidationFailed)
            {
                VerificationStatus = UpdatePackageVerificationStatus.Invalid;
            }
            else if (newState == UpdatePackageLifecycleState.Quarantined)
            {
                VerificationStatus = UpdatePackageVerificationStatus.Quarantined;
            }
        }

        public void SetIntegrity(string sha256)
        {
            if (IsImmutableArtifactState())
            {
                throw new InvalidDomainException("PACKAGE_IMMUTABLE", $"Package '{FileName}' in lifecycle state '{LifecycleState}' is immutable and cryptographic metadata cannot be modified.");
            }

            if (string.IsNullOrWhiteSpace(sha256))
            {
                throw new InvalidDomainException("INVALID_HASH", "SHA-256 hash cannot be null or empty.");
            }

            var trimmedHash = sha256.Trim().ToLowerInvariant();

            if (!Sha256Regex.IsMatch(trimmedHash))
            {
                throw new InvalidDomainException("INVALID_HASH_FORMAT", "SHA-256 hash must be a valid 64-character hexadecimal string.");
            }

            SHA256 = trimmedHash;
            UpdatedAt = DateTime.UtcNow;
        }

        public void SetIntegrityAndSignature(string sha256, string signature, string signingKeyId)
        {
            if (IsImmutableArtifactState())
            {
                throw new InvalidDomainException("PACKAGE_IMMUTABLE", $"Package '{FileName}' in lifecycle state '{LifecycleState}' is immutable and cryptographic metadata cannot be modified.");
            }

            if (string.IsNullOrWhiteSpace(sha256))
            {
                throw new InvalidDomainException("INVALID_HASH", "SHA-256 hash cannot be null or empty.");
            }

            var trimmedHash = sha256.Trim().ToLowerInvariant();

            if (!Sha256Regex.IsMatch(trimmedHash))
            {
                throw new InvalidDomainException("INVALID_HASH_FORMAT", "SHA-256 hash must be a valid 64-character hexadecimal string.");
            }

            if (string.IsNullOrWhiteSpace(signature))
            {
                throw new InvalidDomainException("INVALID_SIGNATURE", "Signature cannot be null or empty.");
            }

            if (string.IsNullOrWhiteSpace(signingKeyId))
            {
                throw new InvalidDomainException("INVALID_SIGNING_KEY_ID", "Signing key ID cannot be null or empty.");
            }

            SHA256 = trimmedHash;
            Signature = signature.Trim();
            SigningKeyId = signingKeyId.Trim();
            UpdatedAt = DateTime.UtcNow;
        }

        public void UpdateStorageKeyAndSize(string newStorageKey, long size)
        {
            if (IsImmutableArtifactState())
            {
                throw new InvalidDomainException("PACKAGE_IMMUTABLE", $"Package '{FileName}' in lifecycle state '{LifecycleState}' is immutable and storage metadata cannot be modified.");
            }

            if (size <= 0)
            {
                throw new InvalidDomainException("INVALID_PACKAGE_SIZE", "Package size must be strictly greater than 0 bytes.");
            }

            StorageKey = ValidateAndNormalizeStorageKey(newStorageKey);
            Size = size;
            UpdatedAt = DateTime.UtcNow;
        }

        public void SetVerificationStatus(UpdatePackageVerificationStatus status)
        {
            VerificationStatus = status;
            UpdatedAt = DateTime.UtcNow;

            if (status == UpdatePackageVerificationStatus.Invalid)
            {
                LifecycleState = UpdatePackageLifecycleState.ValidationFailed;
            }
            else if (status == UpdatePackageVerificationStatus.Quarantined)
            {
                LifecycleState = UpdatePackageLifecycleState.Quarantined;
            }
        }

        public bool IsImmutableArtifactState()
        {
            return LifecycleState is UpdatePackageLifecycleState.Signed
                        or UpdatePackageLifecycleState.Ready
                        or UpdatePackageLifecycleState.Quarantined;
        }
    }
}
