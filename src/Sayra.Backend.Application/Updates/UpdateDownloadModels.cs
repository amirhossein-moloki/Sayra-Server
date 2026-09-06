using System;
using System.IO;
using Sayra.Backend.Application.Abstractions.Security;

#nullable enable

namespace Sayra.Backend.Application.Updates
{
    public class UpdateDownloadRequest
    {
        public UserPrincipal Principal { get; set; } = null!;
        public Guid PackageId { get; set; }
        public string? RangeHeader { get; set; }
        public string? ReportedVersion { get; set; }
        public string? OsVersion { get; set; }
        public string? Architecture { get; set; }
    }

    public class UpdateDownloadRange
    {
        public long Start { get; set; }
        public long End { get; set; }
        public long TotalSize { get; set; }
        public bool IsRangeRequest { get; set; }
        public bool IsSatisfiable { get; set; }
        public string ContentRangeHeader { get; set; } = string.Empty;

        public long ServedLength => IsSatisfiable && TotalSize > 0 ? (End - Start + 1) : 0;
    }

    public class UpdateDownloadPreparation : IDisposable
    {
        public bool IsSuccess { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public int HttpStatusCode { get; set; } = 200;

        public Guid PackageId { get; set; }
        public Guid ReleaseId { get; set; }
        public string Version { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long TotalSize { get; set; }
        public UpdateDownloadRange Range { get; set; } = new UpdateDownloadRange();
        public string ChecksumSha256 { get; set; } = string.Empty;
        public string? Signature { get; set; }
        public string StorageKey { get; set; } = string.Empty;
        public Stream? ContentStream { get; set; }

        public static UpdateDownloadPreparation Success(
            Guid packageId,
            Guid releaseId,
            string version,
            string fileName,
            long totalSize,
            UpdateDownloadRange range,
            string checksumSha256,
            string? signature,
            string storageKey,
            Stream contentStream)
        {
            return new UpdateDownloadPreparation
            {
                IsSuccess = true,
                HttpStatusCode = range.IsRangeRequest ? 206 : 200,
                PackageId = packageId,
                ReleaseId = releaseId,
                Version = version,
                FileName = fileName,
                TotalSize = totalSize,
                Range = range,
                ChecksumSha256 = checksumSha256,
                Signature = signature,
                StorageKey = storageKey,
                ContentStream = contentStream
            };
        }

        public static UpdateDownloadPreparation Failure(
            int statusCode,
            string errorCode,
            string errorMessage,
            UpdateDownloadRange? range = null)
        {
            return new UpdateDownloadPreparation
            {
                IsSuccess = false,
                HttpStatusCode = statusCode,
                ErrorCode = errorCode,
                ErrorMessage = errorMessage,
                Range = range ?? new UpdateDownloadRange { IsSatisfiable = false }
            };
        }

        public void Dispose()
        {
            ContentStream?.Dispose();
            ContentStream = null;
        }
    }
}
