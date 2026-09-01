using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Caching;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
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
        private readonly IRepository<TelemetryMetric> _telemetryRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IRedisService _redisService;

        public IngestTelemetryCommandHandler(
            IRepository<TelemetryMetric> telemetryRepository,
            IUnitOfWork unitOfWork,
            IRedisService redisService)
        {
            _telemetryRepository = telemetryRepository ?? throw new ArgumentNullException(nameof(telemetryRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _redisService = redisService ?? throw new ArgumentNullException(nameof(redisService));
        }

        public async Task<Result<bool>> HandleAsync(IngestTelemetryCommand command, CancellationToken cancellationToken = default)
        {
            if (command == null || command.Telemetry == null)
            {
                return Result<bool>.Failure("Telemetry data cannot be null.");
            }

            var t = command.Telemetry;

            // Strict Validation
            if (t.Cpu < 0 || t.Cpu > 100)
            {
                return Result<bool>.Failure("CPU usage must be between 0% and 100%.");
            }

            if (t.Ram < 0)
            {
                return Result<bool>.Failure("RAM usage cannot be negative.");
            }

            if (t.Uptime < 0)
            {
                return Result<bool>.Failure("Uptime cannot be negative.");
            }

            if (t.RunningGameCpu.HasValue && (t.RunningGameCpu.Value < 0 || t.RunningGameCpu.Value > 100))
            {
                return Result<bool>.Failure("Running game CPU usage must be between 0% and 100%.");
            }

            if (t.RunningGameRam.HasValue && t.RunningGameRam.Value < 0)
            {
                return Result<bool>.Failure("Running game RAM usage cannot be negative.");
            }

            if (!string.IsNullOrEmpty(t.RunningGameName) && t.RunningGameName.Length > 256)
            {
                return Result<bool>.Failure("Running game name exceeds maximum allowed length of 256 characters.");
            }

            var serverReceivedAt = DateTime.UtcNow;

            // Cache latest snapshot in Redis
            if (!string.IsNullOrEmpty(command.PcId))
            {
                string redisKey = $"v1:telemetry:{command.PcId.Trim().ToUpperInvariant()}:latest";
                var snapshot = new
                {
                    command.WorkstationId,
                    command.PcId,
                    t.Cpu,
                    t.Ram,
                    t.Uptime,
                    t.RunningGameName,
                    t.RunningGamePid,
                    t.RunningGameCpu,
                    t.RunningGameRam,
                    t.RunningGameDuration,
                    t.TotalLaunches,
                    t.TotalCrashes,
                    t.TotalRestarts,
                    ClientTimestamp = t.Timestamp,
                    ServerReceivedAt = serverReceivedAt
                };

                await _redisService.SetAsync(redisKey, snapshot, TimeSpan.FromMinutes(15));
            }

            // Persist metrics in PostgreSQL
            if (command.WorkstationId != Guid.Empty)
            {
                var metric = new TelemetryMetric
                {
                    WorkstationId = command.WorkstationId,
                    MetricName = "SystemUsage",
                    MetricValue = t.Cpu,
                    Timestamp = serverReceivedAt,
                    DimensionJson = JsonSerializer.Serialize(new
                    {
                        Ram = t.Ram,
                        Uptime = t.Uptime,
                        GameName = t.RunningGameName,
                        GameCpu = t.RunningGameCpu,
                        GameRam = t.RunningGameRam
                    })
                };

                await _telemetryRepository.AddAsync(metric, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return Result<bool>.Success(true);
        }
    }
}
