using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Sayra.Backend.Application.Abstractions.Backup
{
    public interface IBackupAndDisasterRecoveryService
    {
        Task<BackupMetadata> CreateBackupAsync(BackupRequest request, CancellationToken cancellationToken = default);
        Task<BackupIntegrityResult> VerifyBackupIntegrityAsync(string backupIdOrPath, string? backupDir = null, CancellationToken cancellationToken = default);
        Task<RestoreResult> RestoreDatabaseAsync(RestoreRequest request, CancellationToken cancellationToken = default);
        Task<DatabaseIntegrityReport> ValidateRestoredDatabaseIntegrityAsync(object dbContext, CancellationToken cancellationToken = default);
        Task<BackupFreshnessStatus> EvaluateBackupFreshnessAsync(TimeSpan rpoThreshold, string? backupDir = null, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<BackupMetadata>> GetBackupCatalogAsync(string? backupDir = null, CancellationToken cancellationToken = default);
        Task<PruneResult> ApplyRetentionPolicyAsync(BackupRetentionPolicy policy, CancellationToken cancellationToken = default);
        Task<PruneResult> ApplyRetentionPolicyAsync(string? backupDir, BackupRetentionPolicy policy, CancellationToken cancellationToken = default);
    }
}
