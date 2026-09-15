using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using Sayra.Backend.Application.Abstractions.Transport;

#nullable enable

namespace Sayra.Backend.Infrastructure.Diagnostics
{
    public class TransportMetrics : ITransportMetrics
    {
        public const string MeterName = "Sayra.Backend.Transport";

        private readonly Meter _meter;
        private readonly Counter<long> _acceptedConnectionsCounter;
        private readonly UpDownCounter<long> _activeConnectionsCounter;
        private readonly Counter<long> _rejectedConnectionsCounter;
        private readonly UpDownCounter<long> _authenticationConcurrencyCounter;
        private readonly Counter<long> _authenticationRejectedCounter;
        private readonly Counter<long> _slowDisconnectsCounter;
        private readonly Counter<long> _oversizedFramesCounter;
        private readonly Counter<long> _httpRateLimitRejectedCounter;

        public TransportMetrics()
        {
            _meter = new Meter(MeterName, "1.0.0");
            _acceptedConnectionsCounter = _meter.CreateCounter<long>("tcp_accepted_connections_total", "connections", "Total accepted TCP connections");
            _activeConnectionsCounter = _meter.CreateUpDownCounter<long>("tcp_active_connections", "connections", "Current active TCP connections");
            _rejectedConnectionsCounter = _meter.CreateCounter<long>("tcp_rejected_connections_total", "rejections", "Total rejected TCP connections");
            _authenticationConcurrencyCounter = _meter.CreateUpDownCounter<long>("tcp_authentication_concurrency", "authentications", "Current active authentications in flight");
            _authenticationRejectedCounter = _meter.CreateCounter<long>("tcp_authentication_rejected_total", "rejections", "Total rejected authentication attempts");
            _slowDisconnectsCounter = _meter.CreateCounter<long>("tcp_slow_disconnects_total", "disconnects", "Total disconnects due to slow I/O or timeouts");
            _oversizedFramesCounter = _meter.CreateCounter<long>("tcp_oversized_frames_total", "frames", "Total oversized TCP frame rejections");
            _httpRateLimitRejectedCounter = _meter.CreateCounter<long>("http_rate_limit_rejected_total", "rejections", "Total HTTP requests rejected by rate limiting");
        }

        public void RecordConnectionAccepted()
        {
            _acceptedConnectionsCounter.Add(1);
        }

        public void RecordConnectionActiveDelta(int delta)
        {
            _activeConnectionsCounter.Add(delta);
        }

        public void RecordConnectionRejected(string reason)
        {
            _rejectedConnectionsCounter.Add(1, new KeyValuePair<string, object?>[]
            {
                new("reason", SanitizeLabel(reason))
            });
        }

        public void RecordAuthenticationConcurrencyDelta(int delta)
        {
            _authenticationConcurrencyCounter.Add(delta);
        }

        public void RecordAuthenticationRejected(string reason)
        {
            _authenticationRejectedCounter.Add(1, new KeyValuePair<string, object?>[]
            {
                new("reason", SanitizeLabel(reason))
            });
        }

        public void RecordSlowDisconnect(string reason)
        {
            _slowDisconnectsCounter.Add(1, new KeyValuePair<string, object?>[]
            {
                new("reason", SanitizeLabel(reason))
            });
        }

        public void RecordOversizedFrame(int frameSize, int limit)
        {
            _oversizedFramesCounter.Add(1, new KeyValuePair<string, object?>[]
            {
                new("frame_size", frameSize),
                new("limit", limit)
            });
        }

        public void RecordHttpRateLimitRejected(string endpoint, string policy)
        {
            _httpRateLimitRejectedCounter.Add(1, new KeyValuePair<string, object?>[]
            {
                new("endpoint", SanitizeLabel(endpoint)),
                new("policy", SanitizeLabel(policy))
            });
        }

        private static string SanitizeLabel(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "unknown";
            string trimmed = input.Trim().ToLowerInvariant();
            return trimmed.Length > 64 ? trimmed.Substring(0, 64) : trimmed;
        }
    }
}
