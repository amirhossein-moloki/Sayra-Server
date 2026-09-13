using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Security;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Domain.Enums;
using Sayra.Backend.Shared;

namespace Sayra.Backend.Application.Telemetry
{
    public class MonitoringQueryService : IMonitoringQueryService
    {
        private readonly IAuthorizationService _authorizationService;
        private readonly IRepository<Workstation> _workstationRepository;
        private readonly IWorkstationStateReader _stateReader;
        private readonly IWorkstationHealthReader _healthReader;
        private readonly IWorkstationHealthStore _healthStore;
        private readonly IIncidentRepository _incidentRepository;
        private readonly ITelemetryHistoryRepository _telemetryHistoryRepository;
        private readonly ITelemetryAggregateRepository _telemetryAggregateRepository;
        private readonly IRepository<AuditEvent> _auditEventRepository;
        private readonly IProcessedEventRepository? _processedEventRepository;
        private readonly IWorkstationStreamStateRepository? _streamStateRepository;
        private readonly OfflineQueue.IDeadLetterEventRepository? _dlqRepository;

        public MonitoringQueryService(
            IAuthorizationService authorizationService,
            IRepository<Workstation> workstationRepository,
            IWorkstationStateReader stateReader,
            IWorkstationHealthReader healthReader,
            IWorkstationHealthStore healthStore,
            IIncidentRepository incidentRepository,
            ITelemetryHistoryRepository telemetryHistoryRepository,
            ITelemetryAggregateRepository telemetryAggregateRepository,
            IRepository<AuditEvent> auditEventRepository,
            IProcessedEventRepository? processedEventRepository = null,
            IWorkstationStreamStateRepository? streamStateRepository = null,
            OfflineQueue.IDeadLetterEventRepository? dlqRepository = null)
        {
            _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
            _workstationRepository = workstationRepository ?? throw new ArgumentNullException(nameof(workstationRepository));
            _stateReader = stateReader ?? throw new ArgumentNullException(nameof(stateReader));
            _healthReader = healthReader ?? throw new ArgumentNullException(nameof(healthReader));
            _healthStore = healthStore ?? throw new ArgumentNullException(nameof(healthStore));
            _incidentRepository = incidentRepository ?? throw new ArgumentNullException(nameof(incidentRepository));
            _telemetryHistoryRepository = telemetryHistoryRepository ?? throw new ArgumentNullException(nameof(telemetryHistoryRepository));
            _telemetryAggregateRepository = telemetryAggregateRepository ?? throw new ArgumentNullException(nameof(telemetryAggregateRepository));
            _auditEventRepository = auditEventRepository ?? throw new ArgumentNullException(nameof(auditEventRepository));
            _processedEventRepository = processedEventRepository;
            _streamStateRepository = streamStateRepository;
            _dlqRepository = dlqRepository;
        }

        public async Task<Result<FleetWorkstationsResponseDto>> GetFleetWorkstationsAsync(
            UserPrincipal principal,
            Guid? siteId = null,
            Guid? organizationId = null,
            WorkstationHealthState? healthState = null,
            string? connectionState = null,
            string? status = null,
            int page = 1,
            int pageSize = 50,
            CancellationToken cancellationToken = default)
        {
            var authCheck = await ValidateUserAndPermissionAsync(principal, PermissionCatalog.ViewWorkstations, cancellationToken);
            if (!authCheck.IsSuccess) return Result<FleetWorkstationsResponseDto>.Failure(authCheck.ErrorCode ?? "UNAUTHORIZED", authCheck.ErrorMessage);

            var scopeCheck = ResolveScope(principal, organizationId, siteId);
            if (!scopeCheck.IsSuccess) return Result<FleetWorkstationsResponseDto>.Failure(scopeCheck.ErrorCode ?? "CROSS_ORGANIZATION_ACCESS_DENIED", scopeCheck.ErrorMessage);

            var effectiveOrgId = scopeCheck.Value.OrganizationId;
            var effectiveSiteId = scopeCheck.Value.SiteId;

            // Fetch workstations from repository
            IReadOnlyList<Workstation> dbWorkstations;
            if (effectiveOrgId.HasValue)
            {
                dbWorkstations = await _workstationRepository.FindAsync(
                    w => w.OrganizationEntityId == effectiveOrgId.Value &&
                         (!effectiveSiteId.HasValue || w.SiteEntityId == effectiveSiteId.Value),
                    track: false,
                    cancellationToken: cancellationToken);
            }
            else
            {
                dbWorkstations = await _workstationRepository.GetAllAsync(track: false, cancellationToken);
            }

            // Fetch real-time states and health results from fast memory/Redis readers
            var states = await _stateReader.GetWorkstationStatesAsync(effectiveSiteId, effectiveOrgId, cancellationToken);
            var stateMap = states.ToDictionary(s => s.PcId, StringComparer.OrdinalIgnoreCase);

            var healthResults = await _healthReader.GetFleetHealthResultsAsync(effectiveSiteId, effectiveOrgId, cancellationToken);
            var healthMap = healthResults.ToDictionary(h => h.Identity.PcId, StringComparer.OrdinalIgnoreCase);

            var activeIncidents = await _incidentRepository.GetActiveIncidentsAsync(effectiveOrgId, effectiveSiteId, cancellationToken);
            var incidentCountMap = activeIncidents
                .Where(i => !string.IsNullOrEmpty(i.PcId))
                .GroupBy(i => i.PcId!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            var items = new List<FleetWorkstationItemDto>();

            foreach (var ws in dbWorkstations)
            {
                if (!string.IsNullOrWhiteSpace(principal?.PcId) &&
                    !string.Equals(ws.PcId, principal.PcId, StringComparison.OrdinalIgnoreCase))
                {
                    continue; // Device identity scope filtering
                }

                stateMap.TryGetValue(ws.PcId, out var rtState);
                healthMap.TryGetValue(ws.PcId, out var healthRes);
                incidentCountMap.TryGetValue(ws.PcId, out var incCount);

                var item = new FleetWorkstationItemDto
                {
                    WorkstationId = ws.Id,
                    PcId = ws.PcId,
                    Name = ws.Name,
                    SiteId = ws.SiteEntityId,
                    OrganizationId = ws.OrganizationEntityId,
                    Status = ws.Status,
                    ConnectionState = rtState?.ConnectionState ?? (rtState?.IsConnected == true ? "ConnectedFresh" : "Disconnected"),
                    IsConnected = rtState?.IsConnected ?? false,
                    LastSeenAt = rtState?.LastSeenAt ?? ws.LastSeen,
                    LastTelemetryReceivedAt = rtState?.LastTelemetryReceivedAt,
                    LastHeartbeatReceivedAt = rtState?.LastHeartbeatReceivedAt,
                    HealthState = healthRes?.HealthState ?? WorkstationHealthState.Unknown,
                    HealthScore = healthRes?.HealthScore ?? 100.0,
                    HealthReasons = healthRes?.Reasons.Select(r => new WorkstationHealthReasonDto
                    {
                        ReasonCode = r.ReasonCode,
                        Message = r.Message,
                        Severity = r.Severity,
                        FirstDetectedAtUtc = r.FirstDetectedAtUtc,
                        Value = r.ObservedValue,
                        Threshold = r.Threshold
                    }).ToList() ?? new List<WorkstationHealthReasonDto>(),
                    ActiveIncidentCount = incCount,
                    ClientVersion = ws.ClientVersion,
                    OsVersion = ws.OsVersion
                };

                // Apply in-memory filters
                if (healthState.HasValue && item.HealthState != healthState.Value) continue;
                if (!string.IsNullOrWhiteSpace(connectionState) &&
                    !string.Equals(item.ConnectionState, connectionState, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrWhiteSpace(status) &&
                    !string.Equals(item.Status, status, StringComparison.OrdinalIgnoreCase)) continue;

                items.Add(item);
            }

            int totalCount = items.Count;
            int safePageSize = Math.Clamp(pageSize, 1, 200);
            int safePage = Math.Max(1, page);
            int skip = (safePage - 1) * safePageSize;

            var pagedItems = items.Skip(skip).Take(safePageSize).ToList();

            var summary = new FleetWorkstationSummaryDto
            {
                TotalTrackedWorkstations = totalCount,
                OnlineCount = items.Count(i => i.IsConnected || string.Equals(i.Status, "ONLINE", StringComparison.OrdinalIgnoreCase) || string.Equals(i.Status, "IN_USE", StringComparison.OrdinalIgnoreCase)),
                OfflineCount = items.Count(i => !i.IsConnected && (string.Equals(i.Status, "OFFLINE", StringComparison.OrdinalIgnoreCase) || i.HealthState == WorkstationHealthState.Offline)),
                HealthyCount = items.Count(i => i.HealthState == WorkstationHealthState.Healthy),
                WarningCount = items.Count(i => i.HealthState == WorkstationHealthState.Warning),
                DegradedCount = items.Count(i => i.HealthState == WorkstationHealthState.Degraded),
                CriticalCount = items.Count(i => i.HealthState == WorkstationHealthState.Critical),
                UnknownCount = items.Count(i => i.HealthState == WorkstationHealthState.Unknown),
                EvaluatedAtUtc = DateTime.UtcNow
            };

            var response = new FleetWorkstationsResponseDto
            {
                Summary = summary,
                Items = pagedItems,
                TotalCount = totalCount,
                Page = safePage,
                PageSize = safePageSize
            };

            return Result<FleetWorkstationsResponseDto>.Success(response);
        }

        public async Task<Result<WorkstationDetailDto>> GetWorkstationDetailAsync(
            UserPrincipal principal,
            Guid workstationId,
            CancellationToken cancellationToken = default)
        {
            var authCheck = await ValidateUserAndPermissionAsync(principal, PermissionCatalog.ViewWorkstations, cancellationToken);
            if (!authCheck.IsSuccess) return Result<WorkstationDetailDto>.Failure(authCheck.ErrorCode ?? "UNAUTHORIZED", authCheck.ErrorMessage);

            var workstation = await _workstationRepository.GetByIdAsync(workstationId, track: false, cancellationToken);
            if (workstation == null)
            {
                return Result<WorkstationDetailDto>.Failure("WORKSTATION_NOT_FOUND", $"Workstation with ID '{workstationId}' was not found.");
            }

            var resourceCheck = await AuthorizeResourceAsync(principal, workstation, cancellationToken);
            if (!resourceCheck.IsSuccess) return Result<WorkstationDetailDto>.Failure(resourceCheck.ErrorCode ?? "CROSS_ORGANIZATION_ACCESS_DENIED", resourceCheck.ErrorMessage);

            var rtState = await _stateReader.GetCurrentStateByWorkstationIdAsync(workstationId, cancellationToken)
                ?? await _stateReader.GetCurrentStateAsync(workstation.PcId, cancellationToken);

            var healthRes = await _healthStore.GetHealthResultByWorkstationIdAsync(workstationId, cancellationToken)
                ?? await _healthStore.GetHealthResultAsync(workstation.PcId, cancellationToken);

            var activeIncidents = await _incidentRepository.GetActiveIncidentsForWorkstationAsync(workstation.PcId, cancellationToken);

            var incidentDtos = activeIncidents.Select(i => new IncidentSummaryDto
            {
                Id = i.Id,
                Fingerprint = i.Fingerprint,
                RuleName = i.RuleCode,
                Severity = i.Severity,
                LifecycleState = i.LifecycleState,
                WorkstationId = i.WorkstationId,
                PcId = i.PcId,
                SiteId = i.SiteId,
                OrganizationId = i.OrganizationId,
                Summary = !string.IsNullOrWhiteSpace(i.Title) ? i.Title : i.Description,
                FirstTriggeredAtUtc = i.FirstTriggeredAtUtc,
                LastObservedAtUtc = i.LastObservedAtUtc,
                ResolvedAtUtc = i.ResolvedAtUtc
            }).ToList();

            LatestWorkstationMetricsDto? latestMetrics = null;
            if (rtState != null)
            {
                latestMetrics = new LatestWorkstationMetricsDto
                {
                    Cpu = rtState.Cpu ?? 0.0,
                    Ram = rtState.Ram ?? 0.0,
                    Uptime = (long)(rtState.Uptime ?? 0.0),
                    TotalLaunches = rtState.TotalLaunches ?? 0,
                    TotalCrashes = rtState.TotalCrashes ?? 0,
                    TotalRestarts = rtState.TotalRestarts ?? 0,
                    RunningGameId = rtState.RunningGameName,
                    GameCpu = rtState.RunningGameCpu,
                    GameRam = rtState.RunningGameRam,
                    GameDuration = (long?)(rtState.RunningGameDuration),
                    TimestampUtc = rtState.LastTelemetryReceivedAt ?? rtState.LastSeenAt ?? DateTime.UtcNow
                };
            }

            var detail = new WorkstationDetailDto
            {
                WorkstationId = workstation.Id,
                PcId = workstation.PcId,
                Name = workstation.Name,
                SiteId = workstation.SiteEntityId,
                OrganizationId = workstation.OrganizationEntityId,
                Hostname = workstation.Hostname,
                IpAddress = workstation.IpAddress,
                MacAddress = workstation.MacAddress,
                Status = workstation.Status,
                ClientVersion = workstation.ClientVersion,
                OsVersion = workstation.OsVersion,
                IsDisabled = workstation.IsDisabled,
                IsProvisioned = workstation.IsProvisioned,
                ProvisionedAt = workstation.ProvisionedAt,
                ConnectionId = rtState?.ConnectionId,
                IsConnected = rtState?.IsConnected ?? false,
                ConnectionState = rtState?.ConnectionState ?? "Disconnected",
                LastSeenAt = rtState?.LastSeenAt ?? workstation.LastSeen,
                LastTelemetryReceivedAt = rtState?.LastTelemetryReceivedAt,
                LastHeartbeatReceivedAt = rtState?.LastHeartbeatReceivedAt,
                LatestMetrics = latestMetrics,
                Health = new WorkstationHealthDetailDto
                {
                    HealthState = healthRes?.HealthState ?? WorkstationHealthState.Unknown,
                    HealthScore = healthRes?.HealthScore ?? 100.0,
                    Reasons = healthRes?.Reasons.Select(r => new WorkstationHealthReasonDto
                    {
                        ReasonCode = r.ReasonCode,
                        Message = r.Message,
                        Severity = r.Severity,
                        FirstDetectedAtUtc = r.FirstDetectedAtUtc,
                        Value = r.ObservedValue,
                        Threshold = r.Threshold
                    }).ToList() ?? new List<WorkstationHealthReasonDto>(),
                    EvaluatedAtUtc = healthRes?.EvaluatedAtUtc ?? DateTime.UtcNow,
                    LastTelemetryReceivedAtUtc = rtState?.LastTelemetryReceivedAt,
                    LastHeartbeatReceivedAtUtc = rtState?.LastHeartbeatReceivedAt,
                    ActiveIncidentCount = activeIncidents.Count
                },
                ActiveIncidents = incidentDtos
            };

            return Result<WorkstationDetailDto>.Success(detail);
        }

        public async Task<Result<WorkstationHealthDetailDto>> GetWorkstationHealthAsync(
            UserPrincipal principal,
            Guid workstationId,
            CancellationToken cancellationToken = default)
        {
            var authCheck = await ValidateUserAndPermissionAsync(principal, PermissionCatalog.ViewWorkstations, cancellationToken);
            if (!authCheck.IsSuccess) return Result<WorkstationHealthDetailDto>.Failure(authCheck.ErrorCode ?? "UNAUTHORIZED", authCheck.ErrorMessage);

            var workstation = await _workstationRepository.GetByIdAsync(workstationId, track: false, cancellationToken);
            if (workstation == null)
            {
                return Result<WorkstationHealthDetailDto>.Failure("WORKSTATION_NOT_FOUND", $"Workstation with ID '{workstationId}' was not found.");
            }

            var resourceCheck = await AuthorizeResourceAsync(principal, workstation, cancellationToken);
            if (!resourceCheck.IsSuccess) return Result<WorkstationHealthDetailDto>.Failure(resourceCheck.ErrorCode ?? "CROSS_ORGANIZATION_ACCESS_DENIED", resourceCheck.ErrorMessage);

            var healthRes = await _healthStore.GetHealthResultByWorkstationIdAsync(workstationId, cancellationToken)
                ?? await _healthStore.GetHealthResultAsync(workstation.PcId, cancellationToken);

            var rtState = await _stateReader.GetCurrentStateByWorkstationIdAsync(workstationId, cancellationToken)
                ?? await _stateReader.GetCurrentStateAsync(workstation.PcId, cancellationToken);

            var activeIncidents = await _incidentRepository.GetActiveIncidentsForWorkstationAsync(workstation.PcId, cancellationToken);

            var dto = new WorkstationHealthDetailDto
            {
                HealthState = healthRes?.HealthState ?? WorkstationHealthState.Unknown,
                HealthScore = healthRes?.HealthScore ?? 100.0,
                Reasons = healthRes?.Reasons.Select(r => new WorkstationHealthReasonDto
                {
                    ReasonCode = r.ReasonCode,
                    Message = r.Message,
                    Severity = r.Severity,
                    FirstDetectedAtUtc = r.FirstDetectedAtUtc,
                    Value = r.ObservedValue,
                    Threshold = r.Threshold
                }).ToList() ?? new List<WorkstationHealthReasonDto>(),
                EvaluatedAtUtc = healthRes?.EvaluatedAtUtc ?? DateTime.UtcNow,
                LastTelemetryReceivedAtUtc = rtState?.LastTelemetryReceivedAt,
                LastHeartbeatReceivedAtUtc = rtState?.LastHeartbeatReceivedAt,
                ActiveIncidentCount = activeIncidents.Count
            };

            return Result<WorkstationHealthDetailDto>.Success(dto);
        }

        public async Task<Result<HistoricalMetricsResponseDto>> GetWorkstationMetricsAsync(
            UserPrincipal principal,
            Guid workstationId,
            DateTime? start = null,
            DateTime? end = null,
            string? resolution = null,
            List<string>? metrics = null,
            string? cursor = null,
            int limit = 100,
            CancellationToken cancellationToken = default)
        {
            var authCheck = await ValidateUserAndPermissionAsync(principal, PermissionCatalog.ViewWorkstations, cancellationToken);
            if (!authCheck.IsSuccess) return Result<HistoricalMetricsResponseDto>.Failure(authCheck.ErrorCode ?? "UNAUTHORIZED", authCheck.ErrorMessage);

            var workstation = await _workstationRepository.GetByIdAsync(workstationId, track: false, cancellationToken);
            if (workstation == null)
            {
                return Result<HistoricalMetricsResponseDto>.Failure("WORKSTATION_NOT_FOUND", $"Workstation with ID '{workstationId}' was not found.");
            }

            var resourceCheck = await AuthorizeResourceAsync(principal, workstation, cancellationToken);
            if (!resourceCheck.IsSuccess) return Result<HistoricalMetricsResponseDto>.Failure(resourceCheck.ErrorCode ?? "CROSS_ORGANIZATION_ACCESS_DENIED", resourceCheck.ErrorMessage);

            var nowUtc = DateTime.UtcNow;
            var startUtc = start ?? nowUtc.AddHours(-1);
            var endUtc = end ?? nowUtc;

            if (endUtc < startUtc)
            {
                return Result<HistoricalMetricsResponseDto>.Failure("INVALID_RANGE", "End time cannot be earlier than start time.");
            }

            var rangeDuration = endUtc - startUtc;
            string normRes = (resolution ?? string.Empty).Trim().ToLowerInvariant();

            if (string.IsNullOrEmpty(normRes))
            {
                if (rangeDuration <= TimeSpan.FromHours(2)) normRes = "raw";
                else if (rangeDuration <= TimeSpan.FromDays(1)) normRes = "1m";
                else if (rangeDuration <= TimeSpan.FromDays(30)) normRes = "5m";
                else normRes = "1h";
            }

            if (normRes != "raw" && normRes != "1m" && normRes != "5m" && normRes != "1h" && normRes != "1d")
            {
                return Result<HistoricalMetricsResponseDto>.Failure("INVALID_RESOLUTION", $"Unsupported resolution '{resolution}'. Supported resolutions: raw, 1m, 5m, 1h, 1d.");
            }

            // Enforce max time range guardrails
            TimeSpan maxRange = normRes switch
            {
                "raw" => TimeSpan.FromDays(7),
                "1m" or "5m" => TimeSpan.FromDays(30),
                "1h" or "1d" => TimeSpan.FromDays(365),
                _ => TimeSpan.FromDays(7)
            };

            if (rangeDuration > maxRange)
            {
                return Result<HistoricalMetricsResponseDto>.Failure("INVALID_RANGE", $"Requested time range of {rangeDuration.TotalDays:F1} days exceeds maximum allowed limit of {maxRange.TotalDays} days for resolution '{normRes}'.");
            }

            int safeLimit = Math.Clamp(limit, 1, 1000);
            var points = new List<HistoricalMetricPointDto>();

            if (normRes == "raw")
            {
                var historyRecords = await _telemetryHistoryRepository.GetHistoryForWorkstationAsync(workstationId, startUtc, endUtc, safeLimit, cancellationToken);
                foreach (var h in historyRecords)
                {
                    points.Add(new HistoricalMetricPointDto
                    {
                        TimestampUtc = h.ServerReceivedAt,
                        Cpu = h.Cpu,
                        Ram = h.Ram,
                        Uptime = (long)h.Uptime,
                        Launches = h.TotalLaunches,
                        Crashes = h.TotalCrashes,
                        Restarts = h.TotalRestarts,
                        SampleCount = 1
                    });
                }
            }
            else
            {
                var aggregateRecords = await _telemetryAggregateRepository.GetAggregatesForWorkstationAsync(workstationId, normRes, startUtc, endUtc, safeLimit, cancellationToken);
                foreach (var a in aggregateRecords)
                {
                    points.Add(new HistoricalMetricPointDto
                    {
                        TimestampUtc = a.WindowStart,
                        Cpu = a.CpuAvg,
                        Ram = a.RamAvg,
                        CpuMin = a.CpuMin,
                        CpuMax = a.CpuMax,
                        CpuP95 = a.CpuP95,
                        RamMin = a.RamMin,
                        RamMax = a.RamMax,
                        RamP95 = a.RamP95,
                        Launches = (long)a.LaunchesDelta,
                        Crashes = (long)a.CrashesDelta,
                        Restarts = (long)a.RestartsDelta,
                        SampleCount = a.SampleCount
                    });
                }
            }

            var response = new HistoricalMetricsResponseDto
            {
                WorkstationId = workstation.Id,
                PcId = workstation.PcId,
                Resolution = normRes,
                Start = startUtc,
                End = endUtc,
                TotalPoints = points.Count,
                Points = points
            };

            return Result<HistoricalMetricsResponseDto>.Success(response);
        }

        public async Task<Result<MonitoringPagedResult<MonitoringEventDto>>> GetMonitoringEventsAsync(
            UserPrincipal principal,
            Guid? workstationId = null,
            Guid? siteId = null,
            Guid? organizationId = null,
            string? eventType = null,
            DateTime? start = null,
            DateTime? end = null,
            int page = 1,
            int pageSize = 50,
            CancellationToken cancellationToken = default)
        {
            var authCheck = await ValidateUserAndPermissionAsync(principal, PermissionCatalog.ViewWorkstations, cancellationToken);
            if (!authCheck.IsSuccess) return Result<MonitoringPagedResult<MonitoringEventDto>>.Failure(authCheck.ErrorCode ?? "UNAUTHORIZED", authCheck.ErrorMessage);

            var scopeCheck = ResolveScope(principal, organizationId, siteId);
            if (!scopeCheck.IsSuccess) return Result<MonitoringPagedResult<MonitoringEventDto>>.Failure(scopeCheck.ErrorCode ?? "CROSS_ORGANIZATION_ACCESS_DENIED", scopeCheck.ErrorMessage);

            var effectiveOrgId = scopeCheck.Value.OrganizationId;
            var effectiveSiteId = scopeCheck.Value.SiteId;

            List<Guid> scopedWorkstationIds = new List<Guid>();
            if (effectiveOrgId.HasValue || effectiveSiteId.HasValue)
            {
                var scopedWorkstations = await _workstationRepository.FindAsync(
                    w => (!effectiveOrgId.HasValue || w.OrganizationEntityId == effectiveOrgId.Value) &&
                         (!effectiveSiteId.HasValue || w.SiteEntityId == effectiveSiteId.Value),
                    track: false,
                    cancellationToken: cancellationToken);
                scopedWorkstationIds = scopedWorkstations.Select(w => w.Id).ToList();
            }

            if (workstationId.HasValue)
            {
                var ws = await _workstationRepository.GetByIdAsync(workstationId.Value, track: false, cancellationToken);
                if (ws != null)
                {
                    var resCheck = await AuthorizeResourceAsync(principal, ws, cancellationToken);
                    if (!resCheck.IsSuccess) return Result<MonitoringPagedResult<MonitoringEventDto>>.Failure(resCheck.ErrorCode ?? "CROSS_ORGANIZATION_ACCESS_DENIED", resCheck.ErrorMessage);
                }
            }

            int safePageSize = Math.Clamp(pageSize, 1, 200);
            int safePage = Math.Max(1, page);
            int skip = (safePage - 1) * safePageSize;

            var normType = (eventType ?? string.Empty).Trim();
            var startUtc = start ?? DateTime.MinValue;
            var endUtc = end ?? DateTime.MaxValue;

            IReadOnlyList<AuditEvent> allEvents;
            if (workstationId.HasValue)
            {
                allEvents = await _auditEventRepository.FindAsync(
                    e => e.WorkstationId == workstationId.Value &&
                         (string.IsNullOrEmpty(normType) || e.EventType == normType) &&
                         e.Timestamp >= startUtc && e.Timestamp <= endUtc,
                    track: false,
                    cancellationToken: cancellationToken);
            }
            else if (scopedWorkstationIds.Count > 0)
            {
                allEvents = await _auditEventRepository.FindAsync(
                    e => e.WorkstationId.HasValue && scopedWorkstationIds.Contains(e.WorkstationId.Value) &&
                         (string.IsNullOrEmpty(normType) || e.EventType == normType) &&
                         e.Timestamp >= startUtc && e.Timestamp <= endUtc,
                    track: false,
                    cancellationToken: cancellationToken);
            }
            else
            {
                allEvents = await _auditEventRepository.FindAsync(
                    e => (string.IsNullOrEmpty(normType) || e.EventType == normType) &&
                         e.Timestamp >= startUtc && e.Timestamp <= endUtc,
                    track: false,
                    cancellationToken: cancellationToken);
            }

            int totalCount = allEvents.Count;
            var pagedEvents = allEvents
                .OrderByDescending(e => e.Timestamp)
                .Skip(skip)
                .Take(safePageSize)
                .Select(e => new MonitoringEventDto
                {
                    Id = e.Id,
                    EventId = e.EventId,
                    EventType = e.EventType,
                    WorkstationId = e.WorkstationId,
                    SessionId = e.SessionId,
                    CorrelationId = e.CorrelationId,
                    Priority = e.Priority,
                    TimestampUtc = e.Timestamp,
                    Payload = e.Payload
                })
                .ToList();

            var result = new MonitoringPagedResult<MonitoringEventDto>
            {
                Items = pagedEvents,
                TotalCount = totalCount,
                Page = safePage,
                PageSize = safePageSize
            };

            return Result<MonitoringPagedResult<MonitoringEventDto>>.Success(result);
        }

        public async Task<Result<MonitoringPagedResult<IncidentSummaryDto>>> GetIncidentsAsync(
            UserPrincipal principal,
            Guid? workstationId = null,
            string? pcId = null,
            Guid? siteId = null,
            Guid? organizationId = null,
            AlertSeverity? severity = null,
            IncidentLifecycleState? state = null,
            string? rule = null,
            bool? activeOnly = null,
            int page = 1,
            int pageSize = 50,
            CancellationToken cancellationToken = default)
        {
            var authCheck = await ValidateUserAndPermissionAsync(principal, PermissionCatalog.ViewWorkstations, cancellationToken);
            if (!authCheck.IsSuccess) return Result<MonitoringPagedResult<IncidentSummaryDto>>.Failure(authCheck.ErrorCode ?? "UNAUTHORIZED", authCheck.ErrorMessage);

            var scopeCheck = ResolveScope(principal, organizationId, siteId);
            if (!scopeCheck.IsSuccess) return Result<MonitoringPagedResult<IncidentSummaryDto>>.Failure(scopeCheck.ErrorCode ?? "CROSS_ORGANIZATION_ACCESS_DENIED", scopeCheck.ErrorMessage);

            var effectiveOrgId = scopeCheck.Value.OrganizationId;
            var effectiveSiteId = scopeCheck.Value.SiteId;

            int safePageSize = Math.Clamp(pageSize, 1, 200);
            int safePage = Math.Max(1, page);
            int skip = (safePage - 1) * safePageSize;

            var (incidents, totalCount) = await _incidentRepository.QueryIncidentsAsync(
                organizationId: effectiveOrgId,
                siteId: effectiveSiteId,
                workstationId: workstationId,
                pcId: pcId,
                severity: severity,
                state: state,
                ruleName: rule,
                activeOnly: activeOnly,
                skip: skip,
                take: safePageSize,
                cancellationToken: cancellationToken);

            var dtos = incidents.Select(i => new IncidentSummaryDto
            {
                Id = i.Id,
                Fingerprint = i.Fingerprint,
                RuleName = i.RuleCode,
                Severity = i.Severity,
                LifecycleState = i.LifecycleState,
                WorkstationId = i.WorkstationId,
                PcId = i.PcId,
                SiteId = i.SiteId,
                OrganizationId = i.OrganizationId,
                Summary = !string.IsNullOrWhiteSpace(i.Title) ? i.Title : i.Description,
                FirstTriggeredAtUtc = i.FirstTriggeredAtUtc,
                LastObservedAtUtc = i.LastObservedAtUtc,
                ResolvedAtUtc = i.ResolvedAtUtc
            }).ToList();

            var result = new MonitoringPagedResult<IncidentSummaryDto>
            {
                Items = dtos,
                TotalCount = totalCount,
                Page = safePage,
                PageSize = safePageSize
            };

            return Result<MonitoringPagedResult<IncidentSummaryDto>>.Success(result);
        }

        public async Task<Result<IncidentDetailDto>> GetIncidentDetailAsync(
            UserPrincipal principal,
            Guid incidentId,
            CancellationToken cancellationToken = default)
        {
            var authCheck = await ValidateUserAndPermissionAsync(principal, PermissionCatalog.ViewWorkstations, cancellationToken);
            if (!authCheck.IsSuccess) return Result<IncidentDetailDto>.Failure(authCheck.ErrorCode ?? "UNAUTHORIZED", authCheck.ErrorMessage);

            var incident = await _incidentRepository.GetIncidentByIdAsync(incidentId, cancellationToken);
            if (incident == null)
            {
                return Result<IncidentDetailDto>.Failure("INCIDENT_NOT_FOUND", $"Incident with ID '{incidentId}' was not found.");
            }

            var resourceCheck = await AuthorizeResourceAsync(principal, incident, cancellationToken);
            if (!resourceCheck.IsSuccess) return Result<IncidentDetailDto>.Failure(resourceCheck.ErrorCode ?? "CROSS_ORGANIZATION_ACCESS_DENIED", resourceCheck.ErrorMessage);

            var dto = new IncidentDetailDto
            {
                Id = incident.Id,
                Fingerprint = incident.Fingerprint,
                RuleName = incident.RuleCode,
                Severity = incident.Severity,
                LifecycleState = incident.LifecycleState,
                WorkstationId = incident.WorkstationId,
                PcId = incident.PcId,
                SiteId = incident.SiteId,
                OrganizationId = incident.OrganizationId,
                Summary = !string.IsNullOrWhiteSpace(incident.Title) ? incident.Title : incident.Description,
                FirstTriggeredAtUtc = incident.FirstTriggeredAtUtc,
                LastObservedAtUtc = incident.LastObservedAtUtc,
                FiringAtUtc = incident.FiringAtUtc,
                ResolvedAtUtc = incident.ResolvedAtUtc,
                Reasons = new List<string> { incident.ReasonCode },
                Evidence = incident.TriggerEvidence,
                PolicyVersion = incident.PolicyVersion ?? string.Empty,
                CorrelationId = null,
                SuppressionKey = incident.SuppressionReason,
                RowVersion = incident.RowVersion
            };

            return Result<IncidentDetailDto>.Success(dto);
        }

        public async Task<Result<OfflineOperationalSummaryDto>> GetOfflineOperationalSummaryAsync(
            UserPrincipal principal,
            Guid? siteId = null,
            Guid? organizationId = null,
            CancellationToken cancellationToken = default)
        {
            var authCheck = await ValidateUserAndPermissionAsync(principal, PermissionCatalog.ViewWorkstations, cancellationToken);
            if (!authCheck.IsSuccess) return Result<OfflineOperationalSummaryDto>.Failure(authCheck.ErrorCode ?? "UNAUTHORIZED", authCheck.ErrorMessage);

            var scopeCheck = ResolveScope(principal, organizationId, siteId);
            if (!scopeCheck.IsSuccess) return Result<OfflineOperationalSummaryDto>.Failure(scopeCheck.ErrorCode ?? "CROSS_ORGANIZATION_ACCESS_DENIED", scopeCheck.ErrorMessage);

            var effectiveOrgId = scopeCheck.Value.OrganizationId;
            var effectiveSiteId = scopeCheck.Value.SiteId;

            var summary = new OfflineOperationalSummaryDto
            {
                OrganizationId = effectiveOrgId,
                SiteId = effectiveSiteId,
                EvaluatedAtUtc = DateTime.UtcNow
            };

            // Resolve Workstation IDs for Tenant Isolation
            HashSet<Guid>? scopedWsIds = null;
            HashSet<string>? scopedPcIds = null;

            if (effectiveOrgId.HasValue || effectiveSiteId.HasValue)
            {
                var scopedWorkstations = await _workstationRepository.FindAsync(
                    w => (!effectiveOrgId.HasValue || w.OrganizationEntityId == effectiveOrgId.Value) &&
                         (!effectiveSiteId.HasValue || w.SiteEntityId == effectiveSiteId.Value),
                    track: false,
                    cancellationToken: cancellationToken);

                scopedWsIds = scopedWorkstations?.Select(w => w.Id).ToHashSet() ?? new HashSet<Guid>();
                scopedPcIds = scopedWorkstations?.Select(w => w.PcId).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            // Query Processed Events scoped to Tenant
            if (_processedEventRepository != null)
            {
                IReadOnlyList<ProcessedEvent> processedEvents;
                if (scopedWsIds != null && scopedPcIds != null)
                {
                    processedEvents = await _processedEventRepository.FindAsync(
                        e => (e.WorkstationId.HasValue && scopedWsIds.Contains(e.WorkstationId.Value)) || scopedPcIds.Contains(e.ClientId),
                        track: false,
                        cancellationToken: cancellationToken);
                }
                else
                {
                    processedEvents = await _processedEventRepository.GetAllAsync(track: false, cancellationToken: cancellationToken);
                }

                summary.TotalProcessedEvents = processedEvents.Count;
                summary.AcceptedCount = processedEvents.Count(e => e.ProcessingStatus == "ACCEPTED" || e.ProcessingStatus == "READY_FOR_RECONCILIATION");
                summary.DuplicateCount = processedEvents.Count(e => e.ProcessingStatus == "DUPLICATE");
                summary.RejectedCount = processedEvents.Count(e => e.ProcessingStatus == "REJECTED");
                summary.ConflictCount = processedEvents.Count(e => e.ProcessingStatus == "CONFLICT" || e.ProcessingStatus == "SEQUENCE_CONFLICT");
                summary.WaitingForSequenceCount = processedEvents.Count(e => e.ProcessingStatus == "WAITING_FOR_SEQUENCE");
            }

            // Query Stream States scoped to Tenant
            if (_streamStateRepository != null)
            {
                IReadOnlyList<WorkstationStreamState> streamStates;
                if (scopedWsIds != null && scopedPcIds != null)
                {
                    streamStates = await _streamStateRepository.FindAsync(
                        s => scopedPcIds.Contains(s.ClientId) || (s.WorkstationId.HasValue && scopedWsIds.Contains(s.WorkstationId.Value)),
                        track: false,
                        cancellationToken: cancellationToken);
                }
                else
                {
                    streamStates = await _streamStateRepository.GetAllAsync(track: false, cancellationToken: cancellationToken);
                }

                summary.TotalStreamStates = streamStates.Count;
                summary.ActiveSequenceGaps = 0;
            }

            // Query DLQ Events
            if (_dlqRepository != null)
            {
                string? siteIdStr = effectiveSiteId.HasValue ? effectiveSiteId.Value.ToString() : null;
                var (dlqItems, totalDlq) = await _dlqRepository.GetPagedAsync(
                    siteId: siteIdStr,
                    organizationId: effectiveOrgId,
                    page: 1,
                    pageSize: 1000,
                    cancellationToken: cancellationToken);

                summary.TotalDlqEvents = totalDlq;
                summary.ActiveDeadLetterCount = dlqItems.Count(d => d.ProcessingStatus == "DEAD_LETTER");
                summary.RecoveredDlqCount = dlqItems.Count(d => d.ProcessingStatus == "RECOVERED");
                summary.RejectedDlqCount = dlqItems.Count(d => d.ProcessingStatus == "REJECTED");
                summary.ExpiredDlqCount = dlqItems.Count(d => d.ProcessingStatus == "EXPIRED");

                var breakdown = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in dlqItems)
                {
                    string code = string.IsNullOrWhiteSpace(item.FailureCode) ? "UNKNOWN" : item.FailureCode;
                    if (!breakdown.ContainsKey(code)) breakdown[code] = 0;
                    breakdown[code]++;
                }
                summary.DlqFailureCodeBreakdown = breakdown;
            }

            return Result<OfflineOperationalSummaryDto>.Success(summary);
        }

        private async Task<Result<bool>> ValidateUserAndPermissionAsync(UserPrincipal principal, string permission, CancellationToken cancellationToken)
        {
            if (principal == null || !principal.IsAuthenticated)
            {
                return Result<bool>.Failure("UNAUTHORIZED", "Authentication is required.");
            }

            if (principal.AccountStatus != UserAccountState.Active)
            {
                return Result<bool>.Failure("ACCOUNT_DISABLED", $"User account is {principal.AccountStatus}.");
            }

            var authResult = await _authorizationService.AuthorizeAsync(principal, permission, null, cancellationToken);
            if (!authResult.IsAllowed)
            {
                return Result<bool>.Failure(authResult.ErrorCode ?? "PERMISSION_DENIED", authResult.FailureReason ?? $"Permission '{permission}' required.");
            }

            return Result<bool>.Success(true);
        }

        private async Task<Result<bool>> AuthorizeResourceAsync(UserPrincipal principal, object resource, CancellationToken cancellationToken)
        {
            var authResult = await _authorizationService.AuthorizeAsync(principal, PermissionCatalog.ViewWorkstations, resource, cancellationToken);
            if (!authResult.IsAllowed)
            {
                return Result<bool>.Failure(authResult.ErrorCode ?? "CROSS_ORGANIZATION_ACCESS_DENIED", authResult.FailureReason ?? "Access to requested resource is denied.");
            }

            return Result<bool>.Success(true);
        }

        private Result<(Guid? OrganizationId, Guid? SiteId)> ResolveScope(UserPrincipal principal, Guid? requestedOrgId, Guid? requestedSiteId)
        {
            bool isAdmin = principal.Roles.Any(r => string.Equals(r, RoleCatalog.Administrator, StringComparison.OrdinalIgnoreCase));

            Guid? effectiveOrgId = requestedOrgId ?? principal.OrganizationId;
            Guid? effectiveSiteId = requestedSiteId ?? principal.SiteId;

            if (!isAdmin)
            {
                if (principal.OrganizationId.HasValue)
                {
                    if (requestedOrgId.HasValue && requestedOrgId.Value != principal.OrganizationId.Value)
                    {
                        return Result<(Guid?, Guid?)>.Failure("CROSS_ORGANIZATION_ACCESS_DENIED", "Cannot access data outside assigned organization.");
                    }
                    effectiveOrgId = principal.OrganizationId.Value;
                }

                if (principal.SiteId.HasValue)
                {
                    if (requestedSiteId.HasValue && requestedSiteId.Value != principal.SiteId.Value)
                    {
                        return Result<(Guid?, Guid?)>.Failure("CROSS_SITE_ACCESS_DENIED", "Cannot access data outside assigned site.");
                    }
                    effectiveSiteId = principal.SiteId.Value;
                }
            }

            return Result<(Guid?, Guid?)>.Success((effectiveOrgId, effectiveSiteId));
        }
    }
}
