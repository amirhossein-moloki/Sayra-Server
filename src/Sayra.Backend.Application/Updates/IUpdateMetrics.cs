using System.Diagnostics;

namespace Sayra.Backend.Application.Updates
{
    public interface IUpdateMetrics
    {
        ActivitySource ActivitySource { get; }

        Activity? StartActivity(string name, ActivityKind kind = ActivityKind.Internal);

        void RecordManifestRequest(string result, string failureReason = "none");

        void RecordDownloadStarted(string rangeMode = "full");

        void RecordDownloadCompleted(string rangeMode = "full", long bytesServed = 0);

        void RecordDownloadFailed(string errorType = "unknown", string rangeMode = "full");

        void RecordDownloadCancelled(string rangeMode = "full");

        void RecordDownloadInvalidRange();

        void RecordReleaseOperation(string operation, string result, string failureCode = "none");

        void RecordPackageOperation(string operation, string result, string failureCode = "none");

        void RecordInfrastructureError(string component, string errorType);
    }
}
