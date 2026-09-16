using System;
using System.Collections.Generic;

namespace Sayra.Backend.Application.Abstractions.Backup
{
    public enum BackupType
    {
        Full = 0,
        Differential = 1,
        PointInTime = 2
    }

    public enum BackupHealthState
    {
        Healthy = 0,
        Warning = 1,
        Critical = 2
    }

    public class BackupRequest
    {
        public string BackupDirectory { get; set; } = string.Empty;
        public BackupType BackupType { get; set; } = BackupType.Full;
        public bool Encrypt { get; set; } = false;
        public string? EncryptionKey { get; set; }
        public string Environment { get; set; } = "Production";
    }

    public class BackupMetadata
    {
        public string BackupId { get; set; } = string.Empty;
        public DateTime TimestampUtc { get; set; }
        public BackupType BackupType { get; set; }
        public string StorageLocation { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public string Sha256Checksum { get; set; } = string.Empty;
        public bool IsEncrypted { get; set; }
        public string? EncryptionAlgorithm { get; set; }
        public string DatabaseVersion { get; set; } = "PostgreSQL 15+";
        public string SchemaVersion { get; set; } = "2026.09.08";
        public Dictionary<string, long> RecordCounts { get; set; } = new();
        public bool IsVerified { get; set; }
        public DateTime? LastVerifiedUtc { get; set; }
        public string Status { get; set; } = "CREATED"; // CREATED, VERIFIED, CORRUPTED, EXPIRED
    }

    public class BackupIntegrityResult
    {
        public bool IsValid { get; set; }
        public bool Sha256Matched { get; set; }
        public bool IsCorrupted { get; set; }
        public bool IsTruncated { get; set; }
        public string Sha256Expected { get; set; } = string.Empty;
        public string Sha256Actual { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
        public DateTime CheckedAtUtc { get; set; } = DateTime.UtcNow;
    }

    public class RestoreRequest
    {
        public string BackupId { get; set; } = string.Empty;
        public string BackupFilePath { get; set; } = string.Empty;
        public string TargetConnectionString { get; set; } = string.Empty;
        public string? DecryptionKey { get; set; }
        public bool PerformIntegrityCheckPostRestore { get; set; } = true;
    }

    public class RestoreResult
    {
        public bool IsSuccess { get; set; }
        public string RestoredBackupId { get; set; } = string.Empty;
        public DateTime RestoredAtUtc { get; set; } = DateTime.UtcNow;
        public int RestoredTableCount { get; set; }
        public long RestoredRecordCount { get; set; }
        public string RestoredSchemaVersion { get; set; } = string.Empty;
        public TimeSpan RestorationDuration { get; set; }
        public DatabaseIntegrityReport? IntegrityReport { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class DatabaseIntegrityReport
    {
        public bool IsHealthy { get; set; }
        public int ViolationsCount => Violations.Count;
        public List<string> Violations { get; set; } = new();
        public DateTime CheckedAtUtc { get; set; } = DateTime.UtcNow;
        public Dictionary<string, long> TableCounts { get; set; } = new();
    }

    public class BackupFreshnessStatus
    {
        public BackupHealthState HealthState { get; set; }
        public DateTime? LastSuccessfulBackupUtc { get; set; }
        public TimeSpan? Age { get; set; }
        public TimeSpan RpoThreshold { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class BackupRetentionPolicy
    {
        public int MaxDailyBackups { get; set; } = 7;
        public int MaxWeeklyBackups { get; set; } = 4;
        public int MaxMonthlyBackups { get; set; } = 12;
        public bool ProtectLastVerifiedCopy { get; set; } = true;
    }

    public class PruneResult
    {
        public int TotalExamined { get; set; }
        public int TotalPruned { get; set; }
        public int TotalRetained { get; set; }
        public List<string> PrunedBackupIds { get; set; } = new();
    }
}
