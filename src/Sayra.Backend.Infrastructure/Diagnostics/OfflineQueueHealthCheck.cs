using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Sayra.Backend.Infrastructure.Persistence;

namespace Sayra.Backend.Infrastructure.Diagnostics
{
    public class OfflineQueueHealthCheck : IHealthCheck
    {
        private readonly SqliteOfflineQueueDbContext _sqliteDbContext;
        private readonly ApplicationDbContext _appDbContext;

        public OfflineQueueHealthCheck(SqliteOfflineQueueDbContext sqliteDbContext, ApplicationDbContext appDbContext)
        {
            _sqliteDbContext = sqliteDbContext ?? throw new ArgumentNullException(nameof(sqliteDbContext));
            _appDbContext = appDbContext ?? throw new ArgumentNullException(nameof(appDbContext));
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                var data = new Dictionary<string, object>();

                // 1. Verify SQLite queue database connectivity
                bool sqliteCanConnect = await _sqliteDbContext.Database.CanConnectAsync(cancellationToken);
                data["SqliteDatabaseConnected"] = sqliteCanConnect;

                if (!sqliteCanConnect)
                {
                    return HealthCheckResult.Degraded("Local SQLite offline queue database is inaccessible.", data: data);
                }

                int pendingQueueItems = await _sqliteDbContext.QueueItems.CountAsync(x => x.Status == "PENDING", cancellationToken);
                int failedQueueItems = await _sqliteDbContext.QueueItems.CountAsync(x => x.Status == "FAILED", cancellationToken);

                data["PendingQueueItems"] = pendingQueueItems;
                data["FailedQueueItems"] = failedQueueItems;

                // 2. Verify server-side DLQ backlog
                int dlqCount = await _appDbContext.DeadLetterEvents.CountAsync(x => x.ProcessingStatus == "DEAD_LETTER", cancellationToken);
                data["ActiveDlqEvents"] = dlqCount;

                if (dlqCount > 1000)
                {
                    return HealthCheckResult.Degraded($"Active DLQ backlog is high ({dlqCount} events requiring administrative review).", data: data);
                }

                return HealthCheckResult.Healthy("Offline queue and reconciliation pipeline is healthy.", data);
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Degraded("Offline queue health check encountered an error.", ex);
            }
        }
    }
}
