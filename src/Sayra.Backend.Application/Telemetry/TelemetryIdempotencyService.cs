using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sayra.Backend.Application.Abstractions.Caching;

namespace Sayra.Backend.Application.Telemetry
{
    public class TelemetryIdempotencyService : ITelemetryIdempotencyService
    {
        private readonly IRedisService _redisService;
        private readonly ILogger<TelemetryIdempotencyService> _logger;

        public TelemetryIdempotencyService(IRedisService redisService, ILogger<TelemetryIdempotencyService>? logger = null)
        {
            _redisService = redisService ?? throw new ArgumentNullException(nameof(redisService));
            _logger = logger ?? NullLogger<TelemetryIdempotencyService>.Instance;
        }

        public async Task<bool> IsDuplicateEventAsync(string eventId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(eventId)) return false;

            try
            {
                string dedupKey = $"v1:event:dedup:{eventId.Trim()}";
                string? existing = await _redisService.GetStringAsync(dedupKey, cancellationToken);
                return !string.IsNullOrEmpty(existing);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis error during IsDuplicateEvent check for EventId {EventId}. Failing open.", eventId);
                return false;
            }
        }

        public async Task MarkEventProcessedAsync(string eventId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(eventId)) return;

            try
            {
                string dedupKey = $"v1:event:dedup:{eventId.Trim()}";
                await _redisService.SetStringAsync(dedupKey, "PROCESSED", TimeSpan.FromHours(24), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis error marking event processed for EventId {EventId}.", eventId);
            }
        }

        public async Task<bool> IsStaleTelemetryAsync(string pcId, DateTime clientTimestamp, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(pcId)) return false;

            try
            {
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
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis error during IsStaleTelemetry check for PC-ID {PcId}. Failing open.", pcId);
            }

            return false;
        }

        public async Task RecordLatestTelemetryTimestampAsync(string pcId, DateTime clientTimestamp, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(pcId)) return;

            try
            {
                string tsKey = $"v1:telemetry:{pcId.Trim().ToUpperInvariant()}:latest_ts";
                await _redisService.SetStringAsync(
                    tsKey,
                    clientTimestamp.Ticks.ToString(CultureInfo.InvariantCulture),
                    TimeSpan.FromMinutes(15),
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis error recording latest telemetry timestamp for PC-ID {PcId}.", pcId);
            }
        }
    }
}
