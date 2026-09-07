using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sayra.Backend.Application.Abstractions.Caching;
using Sayra.Backend.Domain.Enums;
using Sayra.Backend.Domain.Telemetry;

namespace Sayra.Backend.Application.Telemetry
{
    public class WorkstationHealthStore : IWorkstationHealthStore, IWorkstationHealthReader
    {
        private const string HealthIndexRedisKey = "v1:workstations:health:index";

        private readonly IRedisService _redisService;
        private readonly IWorkstationStateReader _stateReader;
        private readonly WorkstationHealthPolicyOptions _options;
        private readonly ILogger<WorkstationHealthStore> _logger;
        private readonly ConcurrentDictionary<string, byte> _knownPcIds = new(StringComparer.OrdinalIgnoreCase);

        public WorkstationHealthStore(
            IRedisService redisService,
            IWorkstationStateReader stateReader,
            IOptions<WorkstationHealthPolicyOptions> options,
            ILogger<WorkstationHealthStore> logger)
        {
            _redisService = redisService ?? throw new ArgumentNullException(nameof(redisService));
            _stateReader = stateReader ?? throw new ArgumentNullException(nameof(stateReader));
            _options = options?.Value ?? new WorkstationHealthPolicyOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task SaveHealthResultAsync(
            WorkstationHealthEvaluationResult result,
            CancellationToken cancellationToken = default)
        {
            if (result == null || result.Identity == null || string.IsNullOrWhiteSpace(result.Identity.PcId)) return;

            try
            {
                var pcIdKey = RedisKeyGenerator.WorkstationHealthKeyByPcId(result.Identity.PcId);
                var ttl = TimeSpan.FromHours(24);
                await _redisService.SetAsync(pcIdKey, result, ttl, cancellationToken);

                if (result.Identity.WorkstationId.HasValue && result.Identity.WorkstationId.Value != Guid.Empty)
                {
                    var wsIdKey = RedisKeyGenerator.WorkstationHealthKey(result.Identity.WorkstationId.Value);
                    await _redisService.SetAsync(wsIdKey, result, ttl, cancellationToken);
                }

                await TrackPcIdInHealthIndexAsync(result.Identity.PcId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist health evaluation result for PC-ID {PcId} in Redis.", result.Identity.PcId);
            }
        }

        public async Task<WorkstationHealthEvaluationResult?> GetHealthResultAsync(
            string pcId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(pcId)) return null;

            try
            {
                var pcIdKey = RedisKeyGenerator.WorkstationHealthKeyByPcId(pcId);
                return await _redisService.GetAsync<WorkstationHealthEvaluationResult>(pcIdKey, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve health result for PC-ID {PcId} from Redis.", pcId);
                return null;
            }
        }

        public async Task<WorkstationHealthEvaluationResult?> GetHealthResultByWorkstationIdAsync(
            Guid workstationId,
            CancellationToken cancellationToken = default)
        {
            if (workstationId == Guid.Empty) return null;

            try
            {
                var wsIdKey = RedisKeyGenerator.WorkstationHealthKey(workstationId);
                return await _redisService.GetAsync<WorkstationHealthEvaluationResult>(wsIdKey, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve health result for Workstation ID {WorkstationId} from Redis.", workstationId);
                return null;
            }
        }

        public async Task<IReadOnlyList<WorkstationHealthEvaluationResult>> GetFleetHealthResultsAsync(
            Guid? siteId = null,
            Guid? organizationId = null,
            CancellationToken cancellationToken = default)
        {
            var results = new List<WorkstationHealthEvaluationResult>();

            // Fetch indexed PC-IDs from Redis health index or state reader fallback
            var trackedPcIds = await _redisService.GetAsync<HashSet<string>>(HealthIndexRedisKey, cancellationToken)
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var states = await _stateReader.GetWorkstationStatesAsync(siteId, organizationId, cancellationToken);
            foreach (var st in states)
            {
                if (!string.IsNullOrWhiteSpace(st.PcId))
                {
                    trackedPcIds.Add(st.PcId.Trim().ToUpperInvariant());
                }
            }

            foreach (var pcId in trackedPcIds)
            {
                var res = await GetHealthResultAsync(pcId, cancellationToken);
                if (res == null) continue;

                // Site & Organization tenant boundary filtering
                if (siteId.HasValue && res.Identity.SiteId != siteId.Value) continue;
                if (organizationId.HasValue && res.Identity.OrganizationId != organizationId.Value) continue;

                results.Add(res);
            }

            return results;
        }

        public async Task<FleetHealthSummary> GetFleetHealthSummaryAsync(
            Guid? siteId = null,
            Guid? organizationId = null,
            CancellationToken cancellationToken = default)
        {
            var nowUtc = DateTime.UtcNow;
            var results = await GetFleetHealthResultsAsync(siteId, organizationId, cancellationToken);

            int healthy = 0;
            int warning = 0;
            int degraded = 0;
            int critical = 0;
            int unknown = 0;
            int offline = 0;
            double totalScore = 0.0;

            foreach (var r in results)
            {
                totalScore += r.HealthScore;
                switch (r.HealthState)
                {
                    case WorkstationHealthState.Healthy:
                        healthy++;
                        break;
                    case WorkstationHealthState.Warning:
                        warning++;
                        break;
                    case WorkstationHealthState.Degraded:
                        degraded++;
                        break;
                    case WorkstationHealthState.Critical:
                        critical++;
                        break;
                    case WorkstationHealthState.Unknown:
                        unknown++;
                        break;
                    case WorkstationHealthState.Offline:
                        offline++;
                        break;
                }
            }

            double avgScore = results.Count > 0 ? totalScore / results.Count : 100.0;

            return new FleetHealthSummary(
                TotalTrackedWorkstations: results.Count,
                HealthyCount: healthy,
                WarningCount: warning,
                DegradedCount: degraded,
                CriticalCount: critical,
                UnknownCount: unknown,
                OfflineCount: offline,
                AverageHealthScore: avgScore,
                EvaluatedAtUtc: nowUtc);
        }

        private async Task TrackPcIdInHealthIndexAsync(string pcId, CancellationToken cancellationToken)
        {
            var normalizedPcId = pcId.Trim().ToUpperInvariant();
            if (_knownPcIds.ContainsKey(normalizedPcId))
            {
                return; // Fast O(1) in-memory check to prevent Redis IOPS thrashing
            }

            try
            {
                var tracked = await _redisService.GetAsync<HashSet<string>>(HealthIndexRedisKey, cancellationToken)
                    ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (tracked.Add(normalizedPcId))
                {
                    await _redisService.SetAsync(HealthIndexRedisKey, tracked, TimeSpan.FromDays(7), cancellationToken);
                }

                _knownPcIds[normalizedPcId] = 1;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update Redis health index for PC-ID {PcId}.", pcId);
            }
        }
    }
}
