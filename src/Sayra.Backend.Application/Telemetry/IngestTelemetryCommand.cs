using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Caching;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Shared;

namespace Sayra.Backend.Application.Telemetry
{
    public record IngestTelemetryCommand(
        Guid WorkstationId,
        string PcId,
        TelemetryModel Telemetry
    ) : ICommand<bool>;

    public class IngestTelemetryCommandHandler : ICommandHandler<IngestTelemetryCommand, bool>
    {
        private readonly ITelemetryIngestionService _ingestionService;
        private readonly IRepository<TelemetryMetric> _telemetryRepository;
        private readonly ITelemetryHistoryRepository _historyRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IRedisService _redisService;

        public IngestTelemetryCommandHandler(
            ITelemetryIngestionService ingestionService,
            IRepository<TelemetryMetric> telemetryRepository,
            ITelemetryHistoryRepository historyRepository,
            IUnitOfWork unitOfWork,
            IRedisService redisService)
        {
            _ingestionService = ingestionService ?? throw new ArgumentNullException(nameof(ingestionService));
            _telemetryRepository = telemetryRepository ?? throw new ArgumentNullException(nameof(telemetryRepository));
            _historyRepository = historyRepository ?? throw new ArgumentNullException(nameof(historyRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _redisService = redisService ?? throw new ArgumentNullException(nameof(redisService));
        }

        public async Task<Result<bool>> HandleAsync(IngestTelemetryCommand command, CancellationToken cancellationToken = default)
        {
            if (command == null || command.Telemetry == null)
            {
                return Result<bool>.Failure("PayloadNull", "Telemetry data cannot be null.");
            }

            var context = new TelemetryConnectionContext(
                connectionId: Guid.NewGuid().ToString(),
                pcId: command.PcId,
                workstationId: command.WorkstationId);

            var ingestionResult = await _ingestionService.IngestTelemetrySnapshotAsync(context, command.Telemetry, cancellationToken);
            if (!ingestionResult.IsAccepted)
            {
                return Result<bool>.Failure(ingestionResult.RejectionReason.ToString(), ingestionResult.ErrorMessage ?? "Telemetry ingestion rejected.");
            }

            var snapshot = ingestionResult.Snapshot;
            if (snapshot == null)
            {
                return Result<bool>.Success(true);
            }

            // Cache latest snapshot in Redis
            if (!string.IsNullOrEmpty(command.PcId))
            {
                string redisKey = $"v1:telemetry:{command.PcId.Trim().ToUpperInvariant()}:latest";
                var redisSnapshot = new
                {
                    WorkstationId = snapshot.Identity.WorkstationId,
                    PcId = snapshot.Identity.PcId,
                    Cpu = snapshot.Cpu,
                    Ram = snapshot.Ram,
                    Uptime = snapshot.Uptime,
                    RunningGameName = snapshot.RunningGameName,
                    RunningGamePid = snapshot.RunningGamePid,
                    RunningGameCpu = snapshot.RunningGameCpu,
                    RunningGameRam = snapshot.RunningGameRam,
                    RunningGameDuration = snapshot.RunningGameDuration,
                    TotalLaunches = snapshot.TotalLaunches,
                    TotalCrashes = snapshot.TotalCrashes,
                    TotalRestarts = snapshot.TotalRestarts,
                    ClientTimestamp = snapshot.ClientTimestamp,
                    ServerReceivedAt = snapshot.ServerReceivedAt
                };

                await _redisService.SetAsync(redisKey, redisSnapshot, TimeSpan.FromMinutes(15));
            }

            // Persist metrics and historical telemetry record in PostgreSQL
            if (command.WorkstationId != Guid.Empty)
            {
                var metric = new TelemetryMetric
                {
                    WorkstationId = command.WorkstationId,
                    MetricName = "SystemUsage",
                    MetricValue = snapshot.Cpu,
                    Timestamp = snapshot.ServerReceivedAt,
                    DimensionJson = JsonSerializer.Serialize(new
                    {
                        Ram = snapshot.Ram,
                        Uptime = snapshot.Uptime,
                        GameName = snapshot.RunningGameName,
                        GameCpu = snapshot.RunningGameCpu,
                        GameRam = snapshot.RunningGameRam
                    })
                };

                await _telemetryRepository.AddAsync(metric, cancellationToken);

                var historyRecord = TelemetryHistoryRecord.FromSnapshot(snapshot, context.ConnectionId);
                await _historyRepository.AddAsync(historyRecord, cancellationToken);

                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return Result<bool>.Success(true);
        }
    }
}
