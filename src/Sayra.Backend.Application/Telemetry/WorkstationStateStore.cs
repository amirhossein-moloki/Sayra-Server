using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sayra.Backend.Application.Abstractions.Caching;
using Sayra.Backend.Application.Abstractions.Transport;
using Sayra.Backend.Domain.Telemetry;
using Sayra.Backend.Domain.ValueObjects;

namespace Sayra.Backend.Application.Telemetry
{
    public class WorkstationStateStore : IWorkstationStateStore, IWorkstationStateReader
    {
        private const string IndexRedisKey = "v1:workstations:index";

        private readonly IRedisService _redisService;
        private readonly ITcpConnectionRegistry _connectionRegistry;
        private readonly WorkstationStateOptions _options;
        private readonly ILogger<WorkstationStateStore> _logger;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _pcIdLocks = new(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim _indexLock = new(1, 1);

        public WorkstationStateStore(
            IRedisService redisService,
            ITcpConnectionRegistry connectionRegistry,
            IOptions<WorkstationStateOptions> options,
            ILogger<WorkstationStateStore> logger)
        {
            _redisService = redisService ?? throw new ArgumentNullException(nameof(redisService));
            _connectionRegistry = connectionRegistry ?? throw new ArgumentNullException(nameof(connectionRegistry));
            _options = options?.Value ?? new WorkstationStateOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        private async Task<SemaphoreSlim> GetLockAsync(string pcId, CancellationToken cancellationToken)
        {
            var key = pcId.Trim().ToUpperInvariant();
            var lockObj = _pcIdLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            await lockObj.WaitAsync(cancellationToken);
            return lockObj;
        }

        public async Task<WorkstationRealTimeState?> GetStateAsync(string pcId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(pcId)) return null;

            try
            {
                string key = RedisKeyGenerator.WorkstationStateKeyByPcId(pcId);
                return await _redisService.GetAsync<WorkstationRealTimeState>(key, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve real-time workstation state for PC-ID {PcId} from Redis.", pcId);
                return null;
            }
        }

        public async Task<WorkstationRealTimeState?> GetStateByWorkstationIdAsync(Guid workstationId, CancellationToken cancellationToken = default)
        {
            if (workstationId == Guid.Empty) return null;

            try
            {
                string key = RedisKeyGenerator.WorkstationStateKey(workstationId);
                var state = await _redisService.GetAsync<WorkstationRealTimeState>(key, cancellationToken);
                if (state != null) return state;

                // Fallback check: look up active connections in registry to resolve PC-ID
                var connection = _connectionRegistry.GetAll()
                    .FirstOrDefault(c => c.PcId != null && Guid.TryParse(c.PcId, out var parsedId) && parsedId == workstationId);

                if (connection != null && !string.IsNullOrWhiteSpace(connection.PcId))
                {
                    return await GetStateAsync(connection.PcId, cancellationToken);
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve real-time workstation state for Workstation ID {WorkstationId} from Redis.", workstationId);
                return null;
            }
        }

        public async Task SaveStateAsync(WorkstationRealTimeState state, CancellationToken cancellationToken = default)
        {
            if (state == null || string.IsNullOrWhiteSpace(state.PcId)) return;

            try
            {
                string pcIdKey = RedisKeyGenerator.WorkstationStateKeyByPcId(state.PcId);
                await _redisService.SetAsync(pcIdKey, state, _options.RedisStateTtl, cancellationToken);

                if (state.WorkstationId.HasValue && state.WorkstationId.Value != Guid.Empty)
                {
                    string wsIdKey = RedisKeyGenerator.WorkstationStateKey(state.WorkstationId.Value);
                    await _redisService.SetAsync(wsIdKey, state, _options.RedisStateTtl, cancellationToken);
                }

                await TrackPcIdInIndexAsync(state.PcId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist real-time workstation state for PC-ID {PcId} in Redis.", state.PcId);
            }
        }

        public async Task UpdateFromTelemetryAsync(
            WorkstationIdentity identity,
            TelemetrySnapshot snapshot,
            string? connectionId = null,
            CancellationToken cancellationToken = default)
        {
            if (identity == null || string.IsNullOrWhiteSpace(identity.PcId) || snapshot == null) return;

            var sem = await GetLockAsync(identity.PcId, cancellationToken);
            try
            {
                var existing = await GetStateAsync(identity.PcId, cancellationToken)
                    ?? new WorkstationRealTimeState(identity);

                // Update Identity context if missing
                if (existing.WorkstationId == null && identity.WorkstationId.HasValue)
                {
                    existing.WorkstationId = identity.WorkstationId;
                }
                if (existing.SiteId == null && identity.SiteId.HasValue)
                {
                    existing.SiteId = identity.SiteId;
                }
                if (existing.OrganizationId == null && identity.OrganizationId.HasValue)
                {
                    existing.OrganizationId = identity.OrganizationId;
                }

                // Connection state
                existing.IsConnected = true;
                if (!string.IsNullOrEmpty(connectionId))
                {
                    existing.ConnectionId = connectionId;
                }

                // Out-of-order protection check: Reject stale/older client timestamps
                if (existing.LastTelemetryClientTimestamp.HasValue && snapshot.ClientTimestamp < existing.LastTelemetryClientTimestamp.Value)
                {
                    _logger.LogInformation("Out-of-order telemetry snapshot received for PC-ID {PcId}. Client timestamp {ClientTs} is older than recorded {LatestTs}. Preserving current telemetry metrics.",
                        identity.PcId, snapshot.ClientTimestamp, existing.LastTelemetryClientTimestamp.Value);

                    existing.LastSeenAt = snapshot.ServerReceivedAt > (existing.LastSeenAt ?? DateTime.MinValue) ? snapshot.ServerReceivedAt : existing.LastSeenAt;
                    existing.LastUpdatedAt = snapshot.ProcessedAt;
                    await SaveStateAsync(existing, cancellationToken);
                    return;
                }

                // Partial Field Update: Apply telemetry metrics
                existing.Cpu = snapshot.Cpu;
                existing.Ram = snapshot.Ram;
                existing.Uptime = snapshot.Uptime;

                if (!string.IsNullOrWhiteSpace(snapshot.RunningGameName))
                {
                    existing.RunningGameName = snapshot.RunningGameName;
                    existing.RunningGamePid = snapshot.RunningGamePid;
                    existing.RunningGameCpu = snapshot.RunningGameCpu;
                    existing.RunningGameRam = snapshot.RunningGameRam;
                    existing.RunningGameDuration = snapshot.RunningGameDuration;
                }

                existing.TotalLaunches = snapshot.TotalLaunches;
                existing.TotalCrashes = snapshot.TotalCrashes;
                existing.TotalRestarts = snapshot.TotalRestarts;

                existing.LastTelemetryReceivedAt = snapshot.ServerReceivedAt;
                existing.LastTelemetryClientTimestamp = snapshot.ClientTimestamp;
                existing.LastSeenAt = snapshot.ServerReceivedAt;
                existing.LastUpdatedAt = snapshot.ProcessedAt;

                await SaveStateAsync(existing, cancellationToken);
            }
            finally
            {
                sem.Release();
            }
        }

        public async Task UpdateFromHeartbeatAsync(
            WorkstationIdentity identity,
            HeartbeatSignal heartbeat,
            string? connectionId = null,
            CancellationToken cancellationToken = default)
        {
            if (identity == null || string.IsNullOrWhiteSpace(identity.PcId) || heartbeat == null) return;

            var sem = await GetLockAsync(identity.PcId, cancellationToken);
            try
            {
                var existing = await GetStateAsync(identity.PcId, cancellationToken)
                    ?? new WorkstationRealTimeState(identity);

                // Update Identity context
                if (existing.WorkstationId == null && identity.WorkstationId.HasValue)
                {
                    existing.WorkstationId = identity.WorkstationId;
                }
                if (existing.SiteId == null && identity.SiteId.HasValue)
                {
                    existing.SiteId = identity.SiteId;
                }
                if (existing.OrganizationId == null && identity.OrganizationId.HasValue)
                {
                    existing.OrganizationId = identity.OrganizationId;
                }

                existing.IsConnected = true;
                if (!string.IsNullOrEmpty(connectionId))
                {
                    existing.ConnectionId = connectionId;
                }

                existing.LastHeartbeatReceivedAt = heartbeat.ServerReceivedAt;
                existing.LastHeartbeatClientTimestamp = heartbeat.ClientTimestamp;
                existing.LastSeenAt = heartbeat.ServerReceivedAt;
                existing.LastUpdatedAt = heartbeat.ProcessedAt;

                await SaveStateAsync(existing, cancellationToken);
            }
            finally
            {
                sem.Release();
            }
        }

        public async Task UpdateFromOperationalEventAsync(
            WorkstationIdentity identity,
            OperationalEventSignal eventSignal,
            CancellationToken cancellationToken = default)
        {
            if (identity == null || string.IsNullOrWhiteSpace(identity.PcId) || eventSignal == null) return;

            var sem = await GetLockAsync(identity.PcId, cancellationToken);
            try
            {
                var existing = await GetStateAsync(identity.PcId, cancellationToken)
                    ?? new WorkstationRealTimeState(identity);

                existing.LastEventId = eventSignal.EventId;
                existing.LastEventType = eventSignal.EventType;
                existing.LastEventOccurredAt = eventSignal.OccurredAt;
                existing.LastSeenAt = eventSignal.ServerReceivedAt;
                existing.LastUpdatedAt = eventSignal.ProcessedAt;

                // Operational Event -> State Mappings
                string normalizedType = (eventSignal.EventType ?? string.Empty).Trim().ToUpperInvariant();

                if (normalizedType is "GAME_STARTED" or "APPLICATION_STARTED")
                {
                    if (!string.IsNullOrWhiteSpace(eventSignal.Payload) && eventSignal.Payload.Contains("name"))
                    {
                        try
                        {
                            using var doc = System.Text.Json.JsonDocument.Parse(eventSignal.Payload);
                            if (doc.RootElement.TryGetProperty("name", out var nameProp) || doc.RootElement.TryGetProperty("game", out nameProp))
                            {
                                existing.RunningGameName = nameProp.GetString();
                            }
                        }
                        catch
                        {
                            // Payload parsing fallback
                        }
                    }
                }
                else if (normalizedType is "GAME_EXITED" or "APPLICATION_EXITED" or "GAME_STOPPED")
                {
                    existing.RunningGameName = null;
                    existing.RunningGamePid = null;
                    existing.RunningGameCpu = null;
                    existing.RunningGameRam = null;
                    existing.RunningGameDuration = null;
                }
                else if (normalizedType is "SESSION_STARTED" or "SESSION_PAUSED" or "SESSION_RESUMED")
                {
                    if (!string.IsNullOrWhiteSpace(eventSignal.SessionId))
                    {
                        existing.CurrentSessionId = eventSignal.SessionId;
                    }
                }
                else if (normalizedType is "SESSION_ENDED" or "SESSION_STOPPED")
                {
                    existing.CurrentSessionId = null;
                }
                else if (normalizedType.StartsWith("UPDATE_"))
                {
                    existing.UpdateState = normalizedType;
                }

                await SaveStateAsync(existing, cancellationToken);
            }
            finally
            {
                sem.Release();
            }
        }

        public async Task UpdateConnectionStateAsync(
            WorkstationIdentity identity,
            string connectionId,
            bool isConnected,
            string? connectionState,
            CancellationToken cancellationToken = default)
        {
            if (identity == null || string.IsNullOrWhiteSpace(identity.PcId)) return;

            var sem = await GetLockAsync(identity.PcId, cancellationToken);
            try
            {
                var existing = await GetStateAsync(identity.PcId, cancellationToken)
                    ?? new WorkstationRealTimeState(identity);

                existing.ConnectionId = connectionId;
                existing.IsConnected = isConnected;
                existing.ConnectionState = connectionState;
                existing.LastSeenAt = DateTime.UtcNow;
                existing.LastUpdatedAt = DateTime.UtcNow;

                await SaveStateAsync(existing, cancellationToken);
            }
            finally
            {
                sem.Release();
            }
        }

        private async Task TrackPcIdInIndexAsync(string pcId, CancellationToken cancellationToken)
        {
            try
            {
                await _indexLock.WaitAsync(cancellationToken);
                try
                {
                    var tracked = await _redisService.GetAsync<HashSet<string>>(IndexRedisKey, cancellationToken)
                        ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    if (tracked.Add(pcId.Trim().ToUpperInvariant()))
                    {
                        await _redisService.SetAsync(IndexRedisKey, tracked, TimeSpan.FromDays(7), cancellationToken);
                    }
                }
                finally
                {
                    _indexLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update Redis workstation index for PC-ID {PcId}.", pcId);
            }
        }

        #region IWorkstationStateReader Implementation

        public Task<WorkstationRealTimeState?> GetCurrentStateAsync(string pcId, CancellationToken cancellationToken = default)
        {
            return GetStateAsync(pcId, cancellationToken);
        }

        public Task<WorkstationRealTimeState?> GetCurrentStateByWorkstationIdAsync(Guid workstationId, CancellationToken cancellationToken = default)
        {
            return GetStateByWorkstationIdAsync(workstationId, cancellationToken);
        }

        public async Task<IReadOnlyList<WorkstationRealTimeState>> GetWorkstationStatesAsync(
            Guid? siteId = null,
            Guid? organizationId = null,
            CancellationToken cancellationToken = default)
        {
            var states = new List<WorkstationRealTimeState>();

            // Fetch indexed PC-IDs from Redis
            var trackedPcIds = await _redisService.GetAsync<HashSet<string>>(IndexRedisKey, cancellationToken)
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Merge active connections from transport registry
            foreach (var conn in _connectionRegistry.GetAll())
            {
                if (!string.IsNullOrWhiteSpace(conn.PcId))
                {
                    trackedPcIds.Add(conn.PcId.Trim().ToUpperInvariant());
                }
            }

            foreach (var pcId in trackedPcIds)
            {
                var state = await GetStateAsync(pcId, cancellationToken);
                if (state == null) continue;

                // Site / Organization tenant filtering
                if (siteId.HasValue && state.SiteId != siteId.Value) continue;
                if (organizationId.HasValue && state.OrganizationId != organizationId.Value) continue;

                states.Add(state);
            }

            return states;
        }

        public async Task<FleetStateSummary> GetFleetSummaryAsync(
            Guid? siteId = null,
            Guid? organizationId = null,
            CancellationToken cancellationToken = default)
        {
            var nowUtc = DateTime.UtcNow;
            var states = await GetWorkstationStatesAsync(siteId, organizationId, cancellationToken);

            int fresh = 0;
            int stale = 0;
            int disconnected = 0;
            int offline = 0;
            int unknown = 0;
            int activeSessions = 0;

            foreach (var state in states)
            {
                var opState = state.EvaluateOperationalState(nowUtc, _options.TelemetryStaleThreshold, _options.OfflineTimeout);
                switch (opState)
                {
                    case WorkstationOperationalState.ConnectedFresh:
                        fresh++;
                        break;
                    case WorkstationOperationalState.ConnectedStale:
                        stale++;
                        break;
                    case WorkstationOperationalState.Disconnected:
                        disconnected++;
                        break;
                    case WorkstationOperationalState.Offline:
                        offline++;
                        break;
                    case WorkstationOperationalState.Unknown:
                        unknown++;
                        break;
                }

                if (!string.IsNullOrWhiteSpace(state.CurrentSessionId))
                {
                    activeSessions++;
                }
            }

            return new FleetStateSummary(
                TotalTrackedWorkstations: states.Count,
                ConnectedFresh: fresh,
                ConnectedStale: stale,
                Disconnected: disconnected,
                Offline: offline,
                Unknown: unknown,
                ActiveSessionsCount: activeSessions,
                EvaluatedAt: nowUtc);
        }

        #endregion
    }
}
