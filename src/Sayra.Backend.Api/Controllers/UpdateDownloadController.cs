using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Updates;

#nullable enable

namespace Sayra.Backend.Api.Controllers
{
    [ApiController]
    [Route("api/updates/download")]
    public class UpdateDownloadController : ControllerBase
    {
        private const int StreamBufferSize = 65536; // 64 KB buffer
        private readonly IUpdateDownloadService _downloadService;
        private readonly IUpdateMetrics? _updateMetrics;

        public UpdateDownloadController(IUpdateDownloadService downloadService, IUpdateMetrics? updateMetrics = null)
        {
            _downloadService = downloadService ?? throw new ArgumentNullException(nameof(downloadService));
            _updateMetrics = updateMetrics;
        }

        [HttpGet("{packageId:guid}")]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> DownloadPackageAsync(
            Guid packageId,
            [FromQuery] string? currentVersion,
            [FromQuery] string? reportedVersion,
            [FromQuery] string? osVersion,
            [FromQuery] string? architecture,
            CancellationToken cancellationToken)
        {
            var principal = HttpContext.Items["UserPrincipal"] as UserPrincipal;
            if (principal == null || !principal.IsAuthenticated)
            {
                return Unauthorized(new { code = "UNAUTHORIZED", message = "Authentication is required to download update packages." });
            }

            string? rangeHeader = Request.Headers["Range"].FirstOrDefault();
            string versionToUse = !string.IsNullOrWhiteSpace(currentVersion)
                ? currentVersion
                : (!string.IsNullOrWhiteSpace(reportedVersion) ? reportedVersion : string.Empty);

            var request = new UpdateDownloadRequest
            {
                Principal = principal,
                PackageId = packageId,
                RangeHeader = rangeHeader,
                ReportedVersion = versionToUse,
                OsVersion = osVersion,
                Architecture = architecture
            };

            using var preparation = await _downloadService.PrepareDownloadAsync(request, cancellationToken);

            if (!preparation.IsSuccess)
            {
                if (preparation.HttpStatusCode == StatusCodes.Status416RangeNotSatisfiable)
                {
                    Response.Headers["Content-Range"] = preparation.Range.ContentRangeHeader;
                    return StatusCode(StatusCodes.Status416RangeNotSatisfiable, new { code = preparation.ErrorCode, message = preparation.ErrorMessage });
                }

                if (preparation.HttpStatusCode == StatusCodes.Status401Unauthorized)
                {
                    return Unauthorized(new { code = preparation.ErrorCode, message = preparation.ErrorMessage });
                }

                if (preparation.HttpStatusCode == StatusCodes.Status403Forbidden)
                {
                    return StatusCode(StatusCodes.Status403Forbidden, new { code = preparation.ErrorCode, message = preparation.ErrorMessage });
                }

                if (preparation.HttpStatusCode == StatusCodes.Status404NotFound)
                {
                    return NotFound(new { code = preparation.ErrorCode, message = preparation.ErrorMessage });
                }

                if (preparation.HttpStatusCode == StatusCodes.Status503ServiceUnavailable)
                {
                    return StatusCode(StatusCodes.Status503ServiceUnavailable, new { code = preparation.ErrorCode, message = preparation.ErrorMessage });
                }

                return StatusCode(preparation.HttpStatusCode, new { code = preparation.ErrorCode ?? "DOWNLOAD_FAILED", message = preparation.ErrorMessage });
            }

            if (preparation.ContentStream == null)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { code = "STREAM_NULL", message = "Artifact content stream was null." });
            }

            // Sanitize filename to prevent header injection or control character corruption
            string sanitizedFileName = SanitizeFileName(preparation.FileName);

            Response.Headers["Accept-Ranges"] = "bytes";
            Response.Headers["ETag"] = $"\"{preparation.ChecksumSha256}\"";
            Response.Headers["Content-Disposition"] = $"attachment; filename=\"{sanitizedFileName}\"";
            Response.ContentType = "application/octet-stream";

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, HttpContext.RequestAborted);
            var activeToken = linkedCts.Token;

            string rangeMode = preparation.Range.IsRangeRequest ? "range" : "full";
            long targetLength = preparation.Range.IsRangeRequest ? preparation.Range.ServedLength : preparation.TotalSize;

            _updateMetrics?.RecordDownloadStarted(rangeMode);

            try
            {
                if (preparation.Range.IsRangeRequest)
                {
                    Response.StatusCode = StatusCodes.Status206PartialContent;
                    Response.Headers["Content-Range"] = preparation.Range.ContentRangeHeader;
                    Response.ContentLength = targetLength;
                }
                else
                {
                    Response.StatusCode = StatusCodes.Status200OK;
                    Response.ContentLength = targetLength;
                }

                long bytesWritten = await StreamBytesAsync(preparation.ContentStream, Response.Body, targetLength, activeToken);
                _updateMetrics?.RecordDownloadCompleted(rangeMode, bytesWritten);
                return new EmptyResult();
            }
            catch (OperationCanceledException)
            {
                _updateMetrics?.RecordDownloadCancelled(rangeMode);
                throw;
            }
            catch (Exception)
            {
                _updateMetrics?.RecordDownloadFailed("stream_write_error", rangeMode);
                throw;
            }
        }

        private static async Task<long> StreamBytesAsync(Stream sourceStream, Stream destinationStream, long bytesToStream, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[StreamBufferSize];
            long bytesRemaining = bytesToStream;
            long totalRead = 0;

            while (bytesRemaining > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int readSize = (int)Math.Min(buffer.Length, bytesRemaining);
                int bytesRead = await sourceStream.ReadAsync(buffer.AsMemory(0, readSize), cancellationToken);

                if (bytesRead <= 0)
                {
                    break;
                }

                await destinationStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                bytesRemaining -= bytesRead;
                totalRead += bytesRead;
            }

            await destinationStream.FlushAsync(cancellationToken);
            return totalRead;
        }

        private static string SanitizeFileName(string original)
        {
            if (string.IsNullOrWhiteSpace(original))
            {
                return "update.spk";
            }

            string clean = Path.GetFileName(original.Trim());
            clean = clean.Replace("\"", string.Empty)
                         .Replace("\r", string.Empty)
                         .Replace("\n", string.Empty)
                         .Replace(";", string.Empty);

            return string.IsNullOrWhiteSpace(clean) ? "update.spk" : clean;
        }
    }
}
