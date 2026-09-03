using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Sayra.Backend.Application.Configuration
{
    public interface IConfigurationMetrics
    {
        ActivitySource ActivitySource { get; }

        Activity? StartActivity(string name, ActivityKind kind = ActivityKind.Internal);

        void RecordFetch(string operation, string result, string payloadType, string failureCode = "none");
        void RecordSyncRequest(string result, string payloadType, string failureCode = "none");
        void RecordPublish(string result, string failureCode = "none");
        void RecordRollback(string result, string failureCode = "none");
        void RecordCacheHit();
        void RecordCacheMiss();
        void RecordValidationFailure(string failureCode);
        void RecordSecurityDenied(string operation, string failureCode);
        void RecordSignatureFailure(string failureCode);
    }
}
