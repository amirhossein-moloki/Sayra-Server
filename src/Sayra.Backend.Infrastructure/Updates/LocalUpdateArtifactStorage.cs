using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sayra.Backend.Application.Updates;
using Sayra.Backend.Domain.Exceptions;
using Sayra.Backend.Infrastructure.Configuration.Options;

#nullable enable

namespace Sayra.Backend.Infrastructure.Updates
{
    public class LocalUpdateArtifactStorage : IUpdateArtifactStorage
    {
        private const int StreamBufferSize = 65536; // 64 KB buffer
        private readonly string _baseStoragePath;
        private readonly ILogger<LocalUpdateArtifactStorage> _logger;
        private readonly Sayra.Backend.Application.Resilience.IResiliencePipeline? _resiliencePipeline;

        public LocalUpdateArtifactStorage(
            IOptions<UpdatesOptions> options,
            ILogger<LocalUpdateArtifactStorage> logger,
            Sayra.Backend.Application.Resilience.IResiliencePipeline? resiliencePipeline = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _resiliencePipeline = resiliencePipeline;

            if (options?.Value == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            var rootPath = string.IsNullOrWhiteSpace(options.Value.LocalUpdateRepositoryPath)
                ? "updates"
                : options.Value.LocalUpdateRepositoryPath;

            _baseStoragePath = Path.GetFullPath(rootPath);

            EnsureDirectoryExists(_baseStoragePath);
        }

        public async Task<string> SaveTemporaryArtifactAsync(Guid packageId, Stream contentStream, CancellationToken cancellationToken = default)
        {
            if (packageId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_PACKAGE_ID", "Package ID cannot be empty.");
            }

            if (contentStream == null)
            {
                throw new ArgumentNullException(nameof(contentStream));
            }

            var relativeTempKey = $"temp/{packageId:N}.tmp";
            var physicalTempPath = ResolvePhysicalPath(relativeTempKey);

            var tempDir = Path.GetDirectoryName(physicalTempPath);
            if (!string.IsNullOrEmpty(tempDir))
            {
                EnsureDirectoryExists(tempDir);
            }

            try
            {
                _logger.LogInformation("Streaming update artifact package {PackageId} to temporary path '{TempPath}'", packageId, physicalTempPath);

                using (var targetStream = new FileStream(
                    physicalTempPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    StreamBufferSize,
                    useAsync: true))
                {
                    await contentStream.CopyToAsync(targetStream, StreamBufferSize, cancellationToken);
                    await targetStream.FlushAsync(cancellationToken);
                }

                return relativeTempKey;
            }
            catch
            {
                // Clean up partial file on stream write failure or cancellation
                TryDeletePhysicalFile(physicalTempPath);
                throw;
            }
        }

        public Task FinalizeArtifactAsync(string tempStorageKey, string finalStorageKey, CancellationToken cancellationToken = default)
        {
            var tempPhysicalPath = ResolvePhysicalPath(tempStorageKey);
            var finalPhysicalPath = ResolvePhysicalPath(finalStorageKey);

            if (!File.Exists(tempPhysicalPath))
            {
                throw new InvalidDomainException("TEMPORARY_ARTIFACT_NOT_FOUND", $"Temporary artifact '{tempStorageKey}' was not found on local storage.");
            }

            var finalDir = Path.GetDirectoryName(finalPhysicalPath);
            if (!string.IsNullOrEmpty(finalDir))
            {
                EnsureDirectoryExists(finalDir);
            }

            _logger.LogInformation("Finalizing update artifact from '{TempKey}' to '{FinalKey}'", tempStorageKey, finalStorageKey);

            try
            {
                File.Move(tempPhysicalPath, finalPhysicalPath, overwrite: true);
                return Task.CompletedTask;
            }
            catch (Exception ex) when (ex is not InvalidDomainException)
            {
                _logger.LogError(ex, "Failed to move temporary artifact from '{TempPath}' to '{FinalPath}'", tempPhysicalPath, finalPhysicalPath);
                throw new InvalidDomainException("STORAGE_TRANSFER_FAILED", $"Failed to promote temporary artifact: {ex.Message}");
            }
        }

        public Task<Stream> OpenReadStreamAsync(string storageKey, CancellationToken cancellationToken = default)
        {
            var physicalPath = ResolvePhysicalPath(storageKey);

            if (!File.Exists(physicalPath))
            {
                throw new InvalidDomainException("ARTIFACT_NOT_FOUND", $"Artifact '{storageKey}' was not found on local storage.");
            }

            Stream stream = new FileStream(
                physicalPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                StreamBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            return Task.FromResult(stream);
        }

        public async Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken = default)
        {
            var physicalPath = ResolvePhysicalPath(storageKey);
            if (_resiliencePipeline != null)
            {
                return await _resiliencePipeline.ExecuteAsync(
                    async ct => File.Exists(physicalPath),
                    Sayra.Backend.Application.Resilience.OperationRetrySafety.SafeRead,
                    "FileStorage",
                    "Exists",
                    cancellationToken);
            }
            return File.Exists(physicalPath);
        }

        public async Task DeleteArtifactAsync(string storageKey, CancellationToken cancellationToken = default)
        {
            var physicalPath = ResolvePhysicalPath(storageKey);
            if (_resiliencePipeline != null)
            {
                await _resiliencePipeline.ExecuteAsync(
                    async ct => TryDeletePhysicalFile(physicalPath),
                    Sayra.Backend.Application.Resilience.OperationRetrySafety.IdempotentWrite,
                    "FileStorage",
                    "DeleteArtifact",
                    cancellationToken);
                return;
            }
            TryDeletePhysicalFile(physicalPath);
        }

        public async Task<long> GetArtifactSizeAsync(string storageKey, CancellationToken cancellationToken = default)
        {
            var physicalPath = ResolvePhysicalPath(storageKey);

            if (!File.Exists(physicalPath))
            {
                throw new InvalidDomainException("ARTIFACT_NOT_FOUND", $"Artifact '{storageKey}' was not found on local storage.");
            }

            if (_resiliencePipeline != null)
            {
                return await _resiliencePipeline.ExecuteAsync(
                    async ct => new FileInfo(physicalPath).Length,
                    Sayra.Backend.Application.Resilience.OperationRetrySafety.SafeRead,
                    "FileStorage",
                    "GetArtifactSize",
                    cancellationToken);
            }

            var fileInfo = new FileInfo(physicalPath);
            return fileInfo.Length;
        }

        private string ResolvePhysicalPath(string storageKey)
        {
            if (string.IsNullOrWhiteSpace(storageKey))
            {
                throw new InvalidDomainException("INVALID_STORAGE_KEY", "Storage key cannot be empty.");
            }

            var normalizedKey = storageKey.Trim().Replace('\\', '/');

            if (normalizedKey.Contains(".."))
            {
                throw new InvalidDomainException("UNSAFE_STORAGE_KEY", $"Storage key '{storageKey}' contains directory traversal sequence '..'.");
            }

            var combinedPath = Path.Combine(_baseStoragePath, normalizedKey);
            var fullPath = Path.GetFullPath(combinedPath);

            var normalizedBase = _baseStoragePath.EndsWith(Path.DirectorySeparatorChar.ToString())
                ? _baseStoragePath
                : _baseStoragePath + Path.DirectorySeparatorChar;

            if (!fullPath.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase) && !fullPath.Equals(_baseStoragePath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDomainException("UNSAFE_STORAGE_KEY", $"Storage key '{storageKey}' attempts path traversal outside base repository path.");
            }

            return fullPath;
        }

        private static void EnsureDirectoryExists(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }

        private void TryDeletePhysicalFile(string physicalPath)
        {
            try
            {
                if (File.Exists(physicalPath))
                {
                    File.Delete(physicalPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete physical artifact file at '{PhysicalPath}'", physicalPath);
            }
        }
    }
}
