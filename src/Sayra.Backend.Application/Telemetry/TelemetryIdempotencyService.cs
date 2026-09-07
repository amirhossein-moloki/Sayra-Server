using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Caching;

namespace Sayra.Backend.Application.Telemetry
{
    public class TelemetryIdempotencyService : ITelemetryIdempotencyService
    {
        private readonly IRedisService _redisService;

        public TelemetryIdempotencyService(IRedisService redisService)
        {
            _redisService = redisService ?? throw new ArgumentNullException(nameof(redisService));
        }

        public async Task<bool> IsDuplicateEventAsync(string eventId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(eventId)) return false;

            string dedupKey = $"v1:event:dedup:{eventId.Trim()}";
            string? existing = await _redisService.GetStringAsync(dedupKey, cancellationToken);
            return !string.IsNullOrEmpty(existing);
        }

        public async Task MarkEventProcessedAsync(string eventId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(eventId)) return;

            string dedupKey = $"v1:event:dedup:{eventId.Trim()}";
            await _redisService.SetStringAsync(dedupKey, "PROCESSED", TimeSpan.FromHours(24), cancellationToken);
        }

        public async Task<bool> IsStaleTelemetryAsync(string pcId, DateTime clientTimestamp, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(pcId)) return false;

            string tsKey = $"v1:telemetry:{pcId.Trim().ToUpperInvariant()}:latest_ts";
            string? rawTs = await _redisService.GetStringAsync(tsKey, cancellationToken);

            if (string.IsNullOrEmpty(rawTs)) return false;

            if (long.TryParse(rawTs, CultureInfo.InvariantCulture, out long lastTicks))
            {
                var lastTs = new DateTime(lastTicks, DateTimeKind.Utc);
                if (clientTimestamp < lastTs)
                {
                    return true;
                }
            }

            return false;
        }

        public async Task RecordLatestTelemetryTimestampAsync(string pcId, DateTime clientTimestamp, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(pcId)) return;

            string tsKey = $"v1:telemetry:{pcId.Trim().ToUpperInvariant()}:latest_ts";
            await _redisService.SetStringAsync(
                tsKey,
                clientTimestamp.Ticks.ToString(CultureInfo.InvariantCulture),
                TimeSpan.FromMinutes(15),
                cancellationToken);
        }
    }
}
