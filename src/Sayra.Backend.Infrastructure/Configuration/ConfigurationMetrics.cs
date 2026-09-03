using System.Diagnostics;
using System.Diagnostics.Metrics;
using Sayra.Backend.Application.Configuration;

namespace Sayra.Backend.Infrastructure.Configuration
{
    public class ConfigurationMetrics : IConfigurationMetrics
    {
        public static readonly string MeterName = "Sayra.Backend.Configuration";
        public static readonly string ActivitySourceName = "Sayra.Backend.Configuration";

        private readonly Meter _meter;

        public ActivitySource ActivitySource { get; }

        public Activity? StartActivity(string name, ActivityKind kind = ActivityKind.Internal)
        {
            return ActivitySource.StartActivity(name, kind);
        }

        private readonly Counter<long> _fetchCounter;
        private readonly Counter<long> _syncRequestCounter;
        private readonly Counter<long> _publishCounter;
        private readonly Counter<long> _rollbackCounter;
        private readonly Counter<long> _cacheHitCounter;
        private readonly Counter<long> _cacheMissCounter;
        private readonly Counter<long> _validationFailureCounter;
        private readonly Counter<long> _securityDeniedCounter;
        private readonly Counter<long> _signatureFailureCounter;

        public ConfigurationMetrics()
        {
            _meter = new Meter(MeterName, "1.0.0");
            ActivitySource = new ActivitySource(ActivitySourceName, "1.0.0");

            _fetchCounter = _meter.CreateCounter<long>("configuration_fetch_total", "Count", "Total configuration fetch operations");
            _syncRequestCounter = _meter.CreateCounter<long>("configuration_sync_request_total", "Count", "Total configuration sync requests");
            _publishCounter = _meter.CreateCounter<long>("configuration_publish_total", "Count", "Total configuration publish operations");
            _rollbackCounter = _meter.CreateCounter<long>("configuration_rollback_total", "Count", "Total configuration rollback operations");
            _cacheHitCounter = _meter.CreateCounter<long>("configuration_cache_hit", "Count", "Total configuration cache hits");
            _cacheMissCounter = _meter.CreateCounter<long>("configuration_cache_miss", "Count", "Total configuration cache misses");
            _validationFailureCounter = _meter.CreateCounter<long>("configuration_validation_failure", "Count", "Total configuration validation failures");
            _securityDeniedCounter = _meter.CreateCounter<long>("configuration_security_denied_total", "Count", "Total security denials during configuration operations");
            _signatureFailureCounter = _meter.CreateCounter<long>("configuration_signature_failure_total", "Count", "Total signature verification failures");
        }

        public void RecordFetch(string operation, string result, string payloadType, string failureCode = "none")
        {
            _fetchCounter.Add(1,
                new KeyValuePair<string, object?>("operation", SanitizeLabel(operation)),
                new KeyValuePair<string, object?>("result", SanitizeLabel(result)),
                new KeyValuePair<string, object?>("payload_type", SanitizeLabel(payloadType)),
                new KeyValuePair<string, object?>("failure_code", SanitizeLabel(failureCode)));
        }

        public void RecordSyncRequest(string result, string payloadType, string failureCode = "none")
        {
            _syncRequestCounter.Add(1,
                new KeyValuePair<string, object?>("result", SanitizeLabel(result)),
                new KeyValuePair<string, object?>("payload_type", SanitizeLabel(payloadType)),
                new KeyValuePair<string, object?>("failure_code", SanitizeLabel(failureCode)));
        }

        public void RecordPublish(string result, string failureCode = "none")
        {
            _publishCounter.Add(1,
                new KeyValuePair<string, object?>("result", SanitizeLabel(result)),
                new KeyValuePair<string, object?>("failure_code", SanitizeLabel(failureCode)));
        }

        public void RecordRollback(string result, string failureCode = "none")
        {
            _rollbackCounter.Add(1,
                new KeyValuePair<string, object?>("result", SanitizeLabel(result)),
                new KeyValuePair<string, object?>("failure_code", SanitizeLabel(failureCode)));
        }

        public void RecordCacheHit() => _cacheHitCounter.Add(1);

        public void RecordCacheMiss() => _cacheMissCounter.Add(1);

        public void RecordValidationFailure(string failureCode)
        {
            _validationFailureCounter.Add(1, new KeyValuePair<string, object?>("failure_code", SanitizeLabel(failureCode)));
        }

        public void RecordSecurityDenied(string operation, string failureCode)
        {
            _securityDeniedCounter.Add(1,
                new KeyValuePair<string, object?>("operation", SanitizeLabel(operation)),
                new KeyValuePair<string, object?>("failure_code", SanitizeLabel(failureCode)));
        }

        public void RecordSignatureFailure(string failureCode)
        {
            _signatureFailureCounter.Add(1, new KeyValuePair<string, object?>("failure_code", SanitizeLabel(failureCode)));
        }

        private static string SanitizeLabel(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "none";
            return value.Trim().ToLowerInvariant();
        }
    }
}
