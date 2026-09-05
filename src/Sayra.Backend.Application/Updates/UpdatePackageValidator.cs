using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Exceptions;

#nullable enable

namespace Sayra.Backend.Application.Updates
{
    public class PackageValidationResult
    {
        public bool IsSuccess { get; set; }
        public bool IsQuarantined { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }

        public static PackageValidationResult Success() => new() { IsSuccess = true };

        public static PackageValidationResult Failure(string errorCode, string errorMessage) => new()
        {
            IsSuccess = false,
            IsQuarantined = false,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };

        public static PackageValidationResult Quarantine(string errorCode, string errorMessage) => new()
        {
            IsSuccess = false,
            IsQuarantined = true,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };
    }

    public interface IUpdatePackageValidator
    {
        void ValidateFilename(string fileName);
        void ValidateSize(long size);
        Task<PackageValidationResult> ValidateStructureAsync(Stream artifactStream, UpdatePackageType packageType, CancellationToken cancellationToken = default);
    }

    public class UpdatePackageValidator : IUpdatePackageValidator
    {
        private static readonly Regex InvalidPathCharsRegex = new(@"[\/\\:\*\?""<>\|]|\.\.", RegexOptions.Compiled);
        private static readonly string[] DangerousExecExtensions = new[] { ".exe", ".bat", ".cmd", ".ps1", ".vbs", ".sh", ".dll", ".so", ".dylib" };
        private static readonly byte[] ZipHeaderMagic = new byte[] { 0x50, 0x4B, 0x03, 0x04 }; // "PK\x03\x04"

        private readonly long _maxSizeBytes;
        private readonly ILogger<UpdatePackageValidator> _logger;

        public UpdatePackageValidator(
            IOptions<UpdateValidationOptions> options,
            ILogger<UpdatePackageValidator> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _maxSizeBytes = options?.Value?.MaxArtifactSizeBytes > 0
                ? options.Value.MaxArtifactSizeBytes
                : 524_288_000; // 500 MB default
        }

        public void ValidateFilename(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new InvalidDomainException("INVALID_FILE_NAME", "Filename cannot be null or empty.");
            }

            var trimmed = fileName.Trim();

            if (trimmed.Length > 256)
            {
                throw new InvalidDomainException("FILE_NAME_TOO_LONG", "Filename exceeds maximum allowed length of 256 characters.");
            }

            if (trimmed.Contains('\0'))
            {
                throw new InvalidDomainException("UNSAFE_FILE_NAME", "Filename contains illegal null bytes.");
            }

            if (InvalidPathCharsRegex.IsMatch(trimmed))
            {
                throw new InvalidDomainException("UNSAFE_FILE_NAME", $"Filename '{trimmed}' contains path traversal sequences or illegal characters.");
            }

            // Check for dangerous double extensions (e.g., "update.exe.spk")
            var lowerName = trimmed.ToLowerInvariant();
            var parts = lowerName.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 2)
            {
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    var middleExt = "." + parts[i];
                    if (DangerousExecExtensions.Contains(middleExt))
                    {
                        throw new InvalidDomainException("UNSAFE_FILE_NAME", $"Filename '{trimmed}' attempts dangerous double-extension masking with '{middleExt}'.");
                    }
                }
            }
        }

        public void ValidateSize(long size)
        {
            if (size <= 0)
            {
                throw new InvalidDomainException("INVALID_PACKAGE_SIZE", "Package size must be greater than 0 bytes.");
            }

            if (size > _maxSizeBytes)
            {
                throw new InvalidDomainException("PACKAGE_EXCEEDS_SIZE_LIMIT", $"Package size {size} bytes exceeds configured maximum limit of {_maxSizeBytes} bytes.");
            }
        }

        public async Task<PackageValidationResult> ValidateStructureAsync(Stream artifactStream, UpdatePackageType packageType, CancellationToken cancellationToken = default)
        {
            if (artifactStream == null)
            {
                return PackageValidationResult.Failure("NULL_STREAM", "Artifact stream is null.");
            }

            if (artifactStream.CanSeek)
            {
                artifactStream.Position = 0;
            }

            // Inspect magic header bytes
            var headerBuffer = new byte[4];
            int readBytes = await artifactStream.ReadAsync(headerBuffer, 0, headerBuffer.Length, cancellationToken);

            if (readBytes < 4)
            {
                return PackageValidationResult.Failure("INVALID_HEADER", "Package payload is too short to contain a valid container header.");
            }

            if (packageType == UpdatePackageType.Spk || IsZipContainer(headerBuffer))
            {
                if (!IsZipContainer(headerBuffer))
                {
                    return PackageValidationResult.Failure("INVALID_SPK_HEADER", "Package type '.spk' requires a valid Zip container magic signature.");
                }

                if (artifactStream.CanSeek)
                {
                    artifactStream.Position = 0;
                }

                try
                {
                    using var archive = new ZipArchive(artifactStream, ZipArchiveMode.Read, leaveOpen: true);

                    foreach (var entry in archive.Entries)
                    {
                        var entryName = entry.FullName;

                        // Reject path traversal entry names or leading slashes / absolute paths
                        if (entryName.Contains("..") ||
                            entryName.StartsWith("/") ||
                            entryName.StartsWith("\\") ||
                            (entryName.Length > 1 && entryName[1] == ':'))
                        {
                            _logger.LogWarning("Security quarantine trigger: Zip entry '{EntryName}' contains path traversal or absolute path.", entryName);
                            return PackageValidationResult.Quarantine(
                                "SECURITY_QUARANTINE_TRAVERSAL",
                                $"Package entry '{entryName}' contains malicious path traversal or absolute directory escape attempt.");
                        }
                    }
                }
                catch (InvalidDataException ex)
                {
                    _logger.LogWarning(ex, "Failed to parse archive container for update package.");
                    return PackageValidationResult.Failure("MALFORMED_ARCHIVE_CONTAINER", $"Archive container is malformed or corrupted: {ex.Message}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected exception during package structural validation.");
                    return PackageValidationResult.Failure("PACKAGE_VALIDATION_ERROR", $"Unexpected structural validation error: {ex.Message}");
                }
            }

            if (artifactStream.CanSeek)
            {
                artifactStream.Position = 0;
            }

            return PackageValidationResult.Success();
        }

        private static bool IsZipContainer(byte[] headerBytes)
        {
            return headerBytes.Length >= 4 &&
                   headerBytes[0] == ZipHeaderMagic[0] &&
                   headerBytes[1] == ZipHeaderMagic[1] &&
                   headerBytes[2] == ZipHeaderMagic[2] &&
                   headerBytes[3] == ZipHeaderMagic[3];
        }
    }
}
