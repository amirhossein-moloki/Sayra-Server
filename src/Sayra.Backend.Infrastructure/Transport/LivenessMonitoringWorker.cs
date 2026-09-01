using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sayra.Backend.Application.Abstractions.Caching;
using Sayra.Backend.Application.Abstractions.Communication;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Abstractions.Transport;
using Sayra.Backend.Domain;
using Sayra.Backend.Infrastructure.Configuration.Options;
using Sayra.Backend.Infrastructure.Persistence;

namespace Sayra.Backend.Infrastructure.Transport
{
    public class LivenessMonitoringWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ServerOptions _serverOptions;
        private readonly ILogger<LivenessMonitoringWorker> _logger;

        public LivenessMonitoringWorker(
            IServiceScopeFactory scopeFactory,
            IOptions<ServerOptions> serverOptions,
            ILogger<LivenessMonitoringWorker> logger)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _serverOptions = serverOptions?.Value ?? throw new ArgumentNullException(nameof(serverOptions));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            int checkIntervalSeconds = Math.Max(1, _serverOptions.LivenessCheckInterval);
            _logger.LogInformation("LivenessMonitoringWorker started with check interval {Interval}s, heartbeat interval {HbInterval}s, timeout {HbTimeout}s, grace {Grace}s.",
                checkIntervalSeconds, _serverOptions.HeartbeatInterval, _serverOptions.HeartbeatTimeout, _serverOptions.HeartbeatGracePeriod);

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(checkIntervalSeconds));

            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    await PerformLivenessCheckAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("LivenessMonitoringWorker is shutting down gracefully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in LivenessMonitoringWorker execution loop.");
            }
        }

        public async Task PerformLivenessCheckAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var sessionRepo = scope.ServiceProvider.GetRequiredService<ICommunicationSessionRepository>();
                var connectionRegistry = scope.ServiceProvider.GetRequiredService<ITcpConnectionRegistry>();
                var tcpSessionManager = scope.ServiceProvider.GetRequiredService<ITcpSessionManager>();
                var sequenceValidator = scope.ServiceProvider.GetService<ISequenceValidator>();
                var redisService = scope.ServiceProvider.GetService<IRedisService>();
                var dbContext = scope.ServiceProvider.GetService<ApplicationDbContext>();

                var activeSessions = await sessionRepo.GetActiveSessionsAsync(cancellationToken);
                var now = DateTime.UtcNow;

                var degradedThreshold = TimeSpan.FromSeconds(_serverOptions.HeartbeatInterval + _serverOptions.HeartbeatGracePeriod);
                var timeoutThreshold = TimeSpan.FromSeconds(_serverOptions.HeartbeatTimeout);

                foreach (var session in activeSessions)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    var referenceTime = session.LastHeartbeatAt ?? session.LastActivityAt;
                    var elapsed = now - referenceTime;

                    if (elapsed >= timeoutThreshold)
                    {
                        _logger.LogWarning("HEARTBEAT_TIMEOUT: Connection {ConnectionId} (PcId: {PcId}) timed out after {Elapsed:F1}s of inactivity.",
                            session.ConnectionId, session.PcId ?? "N/A", elapsed.TotalSeconds);

                        session.Disconnect("Heartbeat Timeout", now);
                        await sessionRepo.UpdateAsync(session, cancellationToken);

                        // Disconnect TCP Connection
                        await tcpSessionManager.HandleDisconnectAsync(session.ConnectionId, "Heartbeat Timeout", cancellationToken);

                        // Reset sequence validator
                        if (sequenceValidator != null)
                        {
                            sequenceValidator.ResetSession(session.ConnectionId);
                        }

                        // Remove Redis ephemeral state
                        if (redisService != null && Guid.TryParse(session.ConnectionId, out var connGuid))
                        {
                            try
                            {
                                var redisKey = RedisKeyGenerator.ConnectionStateKey(connGuid);
                                await redisService.RemoveAsync(redisKey);
                            }
                            catch (Exception rEx)
                            {
                                _logger.LogWarning(rEx, "Failed to remove Redis state during timeout for connection {ConnectionId}.", session.ConnectionId);
                            }
                        }

                        // Workstation DB update with multi-connection safety
                        if (!string.IsNullOrEmpty(session.PcId) && dbContext != null)
                        {
                            try
                            {
                                string pcIdUpper = session.PcId.Trim().ToUpperInvariant();
                                var activeConnection = connectionRegistry.GetByPcId(pcIdUpper);

                                // If active connection registry now has a DIFFERENT connection for this PcId, skip marking offline!
                                if (activeConnection != null && !string.Equals(activeConnection.ConnectionId, session.ConnectionId, StringComparison.OrdinalIgnoreCase))
                                {
                                    _logger.LogInformation("STALE_CLEANUP_SKIPPED: Disconnected connection {OldConnId} for PcId {PcId} is superseded by active connection {NewConnId}.",
                                        session.ConnectionId, session.PcId, activeConnection.ConnectionId);
                                }
                                else
                                {
                                    var workstation = await dbContext.Workstations.FirstOrDefaultAsync(w => w.PcId == pcIdUpper, cancellationToken);
                                    if (workstation != null && !string.Equals(workstation.Status, "OFFLINE", StringComparison.OrdinalIgnoreCase))
                                    {
                                        workstation.Status = "OFFLINE";
                                        await dbContext.SaveChangesAsync(cancellationToken);
                                        _logger.LogInformation("WORKSTATION_MARKED_OFFLINE: Workstation {PcId} marked Offline due to heartbeat timeout.", session.PcId);
                                    }
                                }
                            }
                            catch (Exception dbEx)
                            {
                                _logger.LogWarning(dbEx, "Failed to update Workstation offline status in DB for PcId {PcId}.", session.PcId);
                            }
                        }
                    }
                    else if (elapsed >= degradedThreshold)
                    {
                        _logger.LogInformation("WORKSTATION_MARKED_STALE: Connection {ConnectionId} (PcId: {PcId}) marked degraded/stale after {Elapsed:F1}s without heartbeat.",
                            session.ConnectionId, session.PcId ?? "N/A", elapsed.TotalSeconds);

                        if (session.State != ConnectionLifecycleState.Degraded)
                        {
                            session.MarkDegraded("Heartbeat Delay", now);
                            await sessionRepo.UpdateAsync(session, cancellationToken);
                        }

                        var tcpConn = connectionRegistry.Get(session.ConnectionId);
                        if (tcpConn != null && tcpConn.State != ConnectionLifecycleState.Degraded)
                        {
                            try
                            {
                                tcpConn.UpdateState(ConnectionLifecycleState.Degraded);
                            }
                            catch
                            {
                                // Ignore invalid transition if already disconnected
                            }
                        }

                        if (!string.IsNullOrEmpty(session.PcId) && dbContext != null)
                        {
                            try
                            {
                                string pcIdUpper = session.PcId.Trim().ToUpperInvariant();
                                var activeConnection = connectionRegistry.GetByPcId(pcIdUpper);

                                if (activeConnection == null || string.Equals(activeConnection.ConnectionId, session.ConnectionId, StringComparison.OrdinalIgnoreCase))
                                {
                                    var workstation = await dbContext.Workstations.FirstOrDefaultAsync(w => w.PcId == pcIdUpper, cancellationToken);
                                    if (workstation != null && string.Equals(workstation.Status, "ONLINE", StringComparison.OrdinalIgnoreCase))
                                    {
                                        workstation.Status = "STALE";
                                        await dbContext.SaveChangesAsync(cancellationToken);
                                        _logger.LogInformation("WORKSTATION_MARKED_STALE: Workstation {PcId} status updated to Stale in database.", session.PcId);
                                    }
                                }
                            }
                            catch (Exception dbEx)
                            {
                                _logger.LogWarning(dbEx, "Failed to update Workstation stale status in DB for PcId {PcId}.", session.PcId);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during periodic liveness evaluation check.");
            }
        }
    }
}