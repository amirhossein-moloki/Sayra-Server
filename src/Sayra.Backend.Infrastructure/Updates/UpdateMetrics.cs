using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Sayra.Backend.Application.Updates;

namespace Sayra.Backend.Infrastructure.Updates
{
    public class UpdateMetrics : IUpdateMetrics
    {
        public static readonly string MeterName = "Sayra.Backend.Updates";
        public static readonly string ActivitySourceName = "Sayra.Backend.Updates";

        private readonly Meter _meter;

        public ActivitySource ActivitySource { get; }

        public Activity? StartActivity(string name, ActivityKind kind = ActivityKind.Internal)
        {
            return ActivitySource.StartActivity(name, kind);
        }

        private readonly Counter<long> _manifestRequestsCounter;
        private readonly Counter<long> _manifestAvailableCounter;
        private readonly Counter<long> _manifestUnavailableCounter;

        private readonly Counter<long> _downloadRequestsCounter;
        private readonly Counter<long> _downloadStartedCounter;
        private readonly Counter<long> _downloadCompletedCounter;
        private readonly Counter<long> _downloadFailedCounter;
        private readonly Counter<long> _downloadCancelledCounter;
        private readonly Counter<long> _downloadBytesCounter;
        private readonly Counter<long> _downloadInvalidRangeCounter;

        private readonly Counter<long> _releaseOperationCounter;
        private readonly Counter<long> _packageOperationCounter;
        private readonly Counter<long> _infrastructureErrorCounter;

        public UpdateMetrics()
        {
            _meter = new Meter(MeterName, "1.0.0");
            ActivitySource = new ActivitySource(ActivitySourceName, "1.0.0");

            _manifestRequestsCounter = _meter.CreateCounter<long>("update_manifest_requests_total", "Count", "Total update manifest requests");
            _manifestAvailableCounter = _meter.CreateCounter<long>("update_manifest_available_total", "Count", "Total update manifest available responses");
            _manifestUnavailableCounter = _meter.CreateCounter<long>("update_manifest_unavailable_total", "Count", "Total update manifest unavailable responses");

            _downloadRequestsCounter = _meter.CreateCounter<long>("update_download_requests_total", "Count", "Total download requests received");
            _downloadStartedCounter = _meter.CreateCounter<long>("update_download_started_total", "Count", "Total downloads started");
            _downloadCompletedCounter = _meter.CreateCounter<long>("update_download_completed_total", "Count", "Total downloads completed successfully");
            _downloadFailedCounter = _meter.CreateCounter<long>("update_download_failed_total", "Count", "Total downloads failed");
            _downloadCancelledCounter = _meter.CreateCounter<long>("update_download_cancelled_total", "Count", "Total downloads cancelled by client");
            _downloadBytesCounter = _meter.CreateCounter<long>("update_download_bytes_total", "Bytes", "Total update package bytes served");
            _downloadInvalidRangeCounter = _meter.CreateCounter<long>("update_download_invalid_range_total", "Count", "Total invalid range header requests");

            _releaseOperationCounter = _meter.CreateCounter<long>("update_releases_operation_total", "Count", "Total release management operations");
            _packageOperationCounter = _meter.CreateCounter<long>("update_packages_operation_total", "Count", "Total package management operations");
            _infrastructureErrorCounter = _meter.CreateCounter<long>("update_infrastructure_errors_total", "Count", "Total update platform infrastructure errors");
        }

        public void RecordManifestRequest(string result, string failureReason = "none")
        {
            var sanitizedResult = SanitizeLabel(result);
            var sanitizedReason = SanitizeLabel(failureReason);

            _manifestRequestsCounter.Add(1,
                new KeyValuePair<string, object?>("result", sanitizedResult),
                new KeyValuePair<string, object?>("reason", sanitizedReason));

            if (sanitizedResult == "available")
            {
                _manifestAvailableCounter.Add(1);
            }
            else
            {
                _manifestUnavailableCounter.Add(1,
                    new KeyValuePair<string, object?>("reason", sanitizedReason));
            }
        }

        public void RecordDownloadStarted(string rangeMode = "full")
        {
            var mode = SanitizeLabel(rangeMode);
            _downloadRequestsCounter.Add(1, new KeyValuePair<string, object?>("range_mode", mode));
            _downloadStartedCounter.Add(1, new KeyValuePair<string, object?>("range_mode", mode));
        }

        public void RecordDownloadCompleted(string rangeMode = "full", long bytesServed = 0)
        {
            var mode = SanitizeLabel(rangeMode);
            _downloadCompletedCounter.Add(1, new KeyValuePair<string, object?>("range_mode", mode));
            if (bytesServed > 0)
            {
                _downloadBytesCounter.Add(bytesServed, new KeyValuePair<string, object?>("range_mode", mode));
            }
        }

        public void RecordDownloadFailed(string errorType = "unknown", string rangeMode = "full")
        {
            var mode = SanitizeLabel(rangeMode);
            var err = SanitizeLabel(errorType);
            _downloadFailedCounter.Add(1,
                new KeyValuePair<string, object?>("error_type", err),
                new KeyValuePair<string, object?>("range_mode", mode));
        }

        public void RecordDownloadCancelled(string rangeMode = "full")
        {
            var mode = SanitizeLabel(rangeMode);
            _downloadCancelledCounter.Add(1, new KeyValuePair<string, object?>("range_mode", mode));
        }

        public void RecordDownloadInvalidRange()
        {
            _downloadInvalidRangeCounter.Add(1);
        }

        public void RecordReleaseOperation(string operation, string result, string failureCode = "none")
        {
            _releaseOperationCounter.Add(1,
                new KeyValuePair<string, object?>("operation", SanitizeLabel(operation)),
                new KeyValuePair<string, object?>("result", SanitizeLabel(result)),
                new KeyValuePair<string, object?>("failure_code", SanitizeLabel(failureCode)));
        }

        public void RecordPackageOperation(string operation, string result, string failureCode = "none")
        {
            _packageOperationCounter.Add(1,
                new KeyValuePair<string, object?>("operation", SanitizeLabel(operation)),
                new KeyValuePair<string, object?>("result", SanitizeLabel(result)),
                new KeyValuePair<string, object?>("failure_code", SanitizeLabel(failureCode)));
        }

        public void RecordInfrastructureError(string component, string errorType)
        {
            _infrastructureErrorCounter.Add(1,
                new KeyValuePair<string, object?>("component", SanitizeLabel(component)),
                new KeyValuePair<string, object?>("error_type", SanitizeLabel(errorType)));
        }

        private static string SanitizeLabel(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "none";
            return value.Trim().ToLowerInvariant();
        }
    }
}
