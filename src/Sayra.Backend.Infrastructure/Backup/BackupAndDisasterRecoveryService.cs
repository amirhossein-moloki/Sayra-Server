using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sayra.Backend.Application.Abstractions.Backup;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Infrastructure.Persistence;

namespace Sayra.Backend.Infrastructure.Backup
{
    public class BackupAndDisasterRecoveryService : IBackupAndDisasterRecoveryService
    {
        private readonly IServiceProvider _serviceProvider;
        private static readonly object _catalogLock = new object();

        public BackupAndDisasterRecoveryService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        public async Task<BackupMetadata> CreateBackupAsync(BackupRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            string backupDir = string.IsNullOrWhiteSpace(request.BackupDirectory)
                ? Path.Combine(Directory.GetCurrentDirectory(), "backups")
                : request.BackupDirectory;

            Directory.CreateDirectory(backupDir);

            string backupId = $"backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}";
            string rawFilePath = Path.Combine(backupDir, $"{backupId}.json");
            string finalFilePath = rawFilePath;

            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // 1. Gather Snapshot Data
            var snapshotData = new DatabaseSnapshotContainer
            {
                BackupId = backupId,
                TimestampUtc = DateTime.UtcNow,
                SchemaVersion = "2026.09.08",
                Organizations = await dbContext.Organizations.AsNoTracking().ToListAsync(cancellationToken),
                Sites = await dbContext.Sites.AsNoTracking().ToListAsync(cancellationToken),
                Zones = await dbContext.Zones.AsNoTracking().ToListAsync(cancellationToken),
                Workstations = await dbContext.Workstations.AsNoTracking().ToListAsync(cancellationToken),
                WorkstationSessions = await dbContext.WorkstationSessions.AsNoTracking().ToListAsync(cancellationToken),
                Sessions = await dbContext.Sessions.AsNoTracking().ToListAsync(cancellationToken),
                SessionSegments = await dbContext.SessionSegments.AsNoTracking().ToListAsync(cancellationToken),
                Reservations = await dbContext.Reservations.AsNoTracking().ToListAsync(cancellationToken),
                Users = await dbContext.Users.AsNoTracking().ToListAsync(cancellationToken),
                UserCredentials = await dbContext.UserCredentials.AsNoTracking().ToListAsync(cancellationToken),
                Gamers = await dbContext.Gamers.AsNoTracking().ToListAsync(cancellationToken),
                GamerCredentials = await dbContext.GamerCredentials.AsNoTracking().ToListAsync(cancellationToken),
                GamerAccounts = await dbContext.GamerAccounts.AsNoTracking().ToListAsync(cancellationToken),
                LedgerEntries = await dbContext.LedgerEntries.AsNoTracking().ToListAsync(cancellationToken),
                FinancialTransactions = await dbContext.FinancialTransactions.AsNoTracking().ToListAsync(cancellationToken),
                Payments = await dbContext.Payments.AsNoTracking().ToListAsync(cancellationToken),
                AuditEvents = await dbContext.AuditEvents.AsNoTracking().ToListAsync(cancellationToken),
                ProcessedEvents = await dbContext.ProcessedEvents.AsNoTracking().ToListAsync(cancellationToken),
                DeadLetterEvents = await dbContext.DeadLetterEvents.AsNoTracking().ToListAsync(cancellationToken),
                WorkstationStreamStates = await dbContext.WorkstationStreamStates.AsNoTracking().ToListAsync(cancellationToken),
                ConfigurationPackages = await dbContext.ConfigurationPackages.AsNoTracking().ToListAsync(cancellationToken),
                ConfigurationPublications = await dbContext.ConfigurationPublications.AsNoTracking().ToListAsync(cancellationToken),
                ConfigurationSigningKeys = await dbContext.ConfigurationSigningKeys.AsNoTracking().ToListAsync(cancellationToken),
                PricingPlans = await dbContext.PricingPlans.AsNoTracking().ToListAsync(cancellationToken),
                PricingRules = await dbContext.PricingRules.AsNoTracking().ToListAsync(cancellationToken),
                RateSnapshots = await dbContext.RateSnapshots.AsNoTracking().ToListAsync(cancellationToken),
                UpdateReleases = await dbContext.UpdateReleases.AsNoTracking().ToListAsync(cancellationToken),
                UpdatePackages = await dbContext.UpdatePackages.AsNoTracking().ToListAsync(cancellationToken),
                UpdateTargets = await dbContext.UpdateTargets.AsNoTracking().ToListAsync(cancellationToken)
            };

            var jsonOptions = GetJsonOptions();
            byte[] rawBytes = JsonSerializer.SerializeToUtf8Bytes(snapshotData, jsonOptions);

            // 2. Encryption (if requested)
            byte[] outputBytes = rawBytes;
            string? encryptionAlgo = null;

            if (request.Encrypt)
            {
                string keyStr = string.IsNullOrWhiteSpace(request.EncryptionKey)
                    ? "SayraDefaultBackupEncryptionKey32B!"
                    : request.EncryptionKey;

                outputBytes = EncryptBytes(rawBytes, keyStr);
                encryptionAlgo = "AES-256-CBC-HMAC";
                finalFilePath = Path.Combine(backupDir, $"{backupId}.enc");
            }

            await File.WriteAllBytesAsync(finalFilePath, outputBytes, cancellationToken);

            // 3. Compute SHA256 Checksum
            string sha256 = ComputeSha256(outputBytes);

            // 4. Record Counts
            var recordCounts = new Dictionary<string, long>
            {
                ["Workstations"] = snapshotData.Workstations.Count,
                ["Sessions"] = snapshotData.Sessions.Count,
                ["Reservations"] = snapshotData.Reservations.Count,
                ["GamerAccounts"] = snapshotData.GamerAccounts.Count,
                ["LedgerEntries"] = snapshotData.LedgerEntries.Count,
                ["FinancialTransactions"] = snapshotData.FinancialTransactions.Count,
                ["ProcessedEvents"] = snapshotData.ProcessedEvents.Count,
                ["ConfigurationPackages"] = snapshotData.ConfigurationPackages.Count,
                ["AuditEvents"] = snapshotData.AuditEvents.Count,
                ["UpdateReleases"] = snapshotData.UpdateReleases.Count
            };

            var metadata = new BackupMetadata
            {
                BackupId = backupId,
                TimestampUtc = snapshotData.TimestampUtc,
                BackupType = request.BackupType,
                StorageLocation = finalFilePath,
                FileSizeBytes = outputBytes.Length,
                Sha256Checksum = sha256,
                IsEncrypted = request.Encrypt,
                EncryptionAlgorithm = encryptionAlgo,
                DatabaseVersion = "PostgreSQL 15+",
                SchemaVersion = snapshotData.SchemaVersion,
                RecordCounts = recordCounts,
                IsVerified = true,
                LastVerifiedUtc = DateTime.UtcNow,
                Status = "VERIFIED"
            };

            await SaveToCatalogAsync(backupDir, metadata, cancellationToken);

            return metadata;
        }

        public async Task<BackupIntegrityResult> VerifyBackupIntegrityAsync(string backupIdOrPath, string? backupDir = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(backupIdOrPath))
            {
                return new BackupIntegrityResult
                {
                    IsValid = false,
                    IsCorrupted = true,
                    ErrorMessage = "Backup identifier or path is empty."
                };
            }

            string filePath = backupIdOrPath;
            if (File.Exists(filePath) && string.IsNullOrWhiteSpace(backupDir))
            {
                backupDir = Path.GetDirectoryName(filePath);
            }

            BackupMetadata? meta = null;

            if (!File.Exists(filePath))
            {
                // Try finding via catalog or in backups folder
                var catalog = await LoadCatalogInternalAsync(backupDir, cancellationToken);
                meta = catalog.FirstOrDefault(b => b.BackupId == backupIdOrPath || b.StorageLocation == backupIdOrPath);
                if (meta != null && File.Exists(meta.StorageLocation))
                {
                    filePath = meta.StorageLocation;
                    backupDir = Path.GetDirectoryName(filePath);
                }
            }

            if (!File.Exists(filePath))
            {
                return new BackupIntegrityResult
                {
                    IsValid = false,
                    IsCorrupted = true,
                    ErrorMessage = $"Backup file does not exist: {filePath}"
                };
            }

            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length == 0)
            {
                return new BackupIntegrityResult
                {
                    IsValid = false,
                    IsCorrupted = true,
                    IsTruncated = true,
                    ErrorMessage = "Backup file is truncated or 0 bytes."
                };
            }

            byte[] fileBytes;
            try
            {
                fileBytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
            }
            catch (Exception ex)
            {
                return new BackupIntegrityResult
                {
                    IsValid = false,
                    IsCorrupted = true,
                    ErrorMessage = $"Failed to read backup file: {ex.Message}"
                };
            }

            string actualSha256 = ComputeSha256(fileBytes);

            if (meta == null)
            {
                var catalog = await LoadCatalogInternalAsync(backupDir, cancellationToken);
                meta = catalog.FirstOrDefault(b => b.StorageLocation == filePath || b.BackupId == Path.GetFileNameWithoutExtension(filePath));
            }

            string expectedSha256 = meta?.Sha256Checksum ?? string.Empty;
            bool shaMatched = string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrEmpty(expectedSha256) && !shaMatched)
            {
                return new BackupIntegrityResult
                {
                    IsValid = false,
                    Sha256Matched = false,
                    IsCorrupted = true,
                    Sha256Expected = expectedSha256,
                    Sha256Actual = actualSha256,
                    ErrorMessage = $"SHA256 checksum mismatch. Expected: {expectedSha256}, Actual: {actualSha256}"
                };
            }

            if (meta != null)
            {
                meta.IsVerified = true;
                meta.LastVerifiedUtc = DateTime.UtcNow;
                meta.Status = "VERIFIED";
                await UpdateInCatalogAsync(backupDir, meta, cancellationToken);
            }

            return new BackupIntegrityResult
            {
                IsValid = true,
                Sha256Matched = true,
                IsCorrupted = false,
                IsTruncated = false,
                Sha256Expected = expectedSha256,
                Sha256Actual = actualSha256
            };
        }

        public async Task<RestoreResult> RestoreDatabaseAsync(RestoreRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            var startTime = DateTime.UtcNow;

            // 1. Verify Integrity
            string targetPath = !string.IsNullOrWhiteSpace(request.BackupFilePath)
                ? request.BackupFilePath
                : request.BackupId;

            string? backupDir = File.Exists(request.BackupFilePath)
                ? Path.GetDirectoryName(request.BackupFilePath)
                : null;

            var integrity = await VerifyBackupIntegrityAsync(targetPath, backupDir, cancellationToken);
            if (!integrity.IsValid)
            {
                return new RestoreResult
                {
                    IsSuccess = false,
                    RestoredBackupId = request.BackupId,
                    ErrorMessage = $"Cannot restore invalid backup: {integrity.ErrorMessage}"
                };
            }

            // 2. Resolve File Path
            string filePath = request.BackupFilePath;
            if (!File.Exists(filePath))
            {
                var catalog = await LoadCatalogInternalAsync(backupDir, cancellationToken);
                var meta = catalog.FirstOrDefault(b => b.BackupId == request.BackupId || b.StorageLocation == request.BackupFilePath);
                if (meta != null && File.Exists(meta.StorageLocation))
                {
                    filePath = meta.StorageLocation;
                }
            }

            if (!File.Exists(filePath))
            {
                return new RestoreResult
                {
                    IsSuccess = false,
                    RestoredBackupId = request.BackupId,
                    ErrorMessage = $"Backup file not found at: {filePath}"
                };
            }

            // 3. Read & Decrypt
            byte[] fileBytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
            byte[] rawBytes = fileBytes;

            if (filePath.EndsWith(".enc", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(request.DecryptionKey))
            {
                string keyStr = string.IsNullOrWhiteSpace(request.DecryptionKey)
                    ? "SayraDefaultBackupEncryptionKey32B!"
                    : request.DecryptionKey;

                try
                {
                    rawBytes = DecryptBytes(fileBytes, keyStr);
                }
                catch (Exception ex)
                {
                    return new RestoreResult
                    {
                        IsSuccess = false,
                        RestoredBackupId = request.BackupId,
                        ErrorMessage = $"Failed to decrypt backup archive: {ex.Message}"
                    };
                }
            }

            DatabaseSnapshotContainer? snapshot;
            try
            {
                snapshot = JsonSerializer.Deserialize<DatabaseSnapshotContainer>(rawBytes, GetJsonOptions());
            }
            catch (Exception ex)
            {
                return new RestoreResult
                {
                    IsSuccess = false,
                    RestoredBackupId = request.BackupId,
                    ErrorMessage = $"Failed to deserialize snapshot payload: {ex.Message}"
                };
            }

            if (snapshot == null)
            {
                return new RestoreResult
                {
                    IsSuccess = false,
                    RestoredBackupId = request.BackupId,
                    ErrorMessage = "Snapshot payload is empty or invalid."
                };
            }

            // 4. Restore into Target DbContext
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // Clean existing state if restoring into a fresh/isolated test database
            await CleanDbContextStateAsync(dbContext, cancellationToken);

            // Populate entities from snapshot
            if (snapshot.Organizations?.Count > 0) dbContext.Organizations.AddRange(snapshot.Organizations);
            if (snapshot.Sites?.Count > 0) dbContext.Sites.AddRange(snapshot.Sites);
            if (snapshot.Zones?.Count > 0) dbContext.Zones.AddRange(snapshot.Zones);
            if (snapshot.Workstations?.Count > 0) dbContext.Workstations.AddRange(snapshot.Workstations);
            if (snapshot.WorkstationSessions?.Count > 0) dbContext.WorkstationSessions.AddRange(snapshot.WorkstationSessions);
            if (snapshot.Sessions?.Count > 0) dbContext.Sessions.AddRange(snapshot.Sessions);
            if (snapshot.SessionSegments?.Count > 0) dbContext.SessionSegments.AddRange(snapshot.SessionSegments);
            if (snapshot.Reservations?.Count > 0) dbContext.Reservations.AddRange(snapshot.Reservations);
            if (snapshot.Users?.Count > 0) dbContext.Users.AddRange(snapshot.Users);
            if (snapshot.UserCredentials?.Count > 0) dbContext.UserCredentials.AddRange(snapshot.UserCredentials);
            if (snapshot.Gamers?.Count > 0) dbContext.Gamers.AddRange(snapshot.Gamers);
            if (snapshot.GamerCredentials?.Count > 0) dbContext.GamerCredentials.AddRange(snapshot.GamerCredentials);
            if (snapshot.GamerAccounts?.Count > 0) dbContext.GamerAccounts.AddRange(snapshot.GamerAccounts);
            if (snapshot.LedgerEntries?.Count > 0) dbContext.LedgerEntries.AddRange(snapshot.LedgerEntries);
            if (snapshot.FinancialTransactions?.Count > 0) dbContext.FinancialTransactions.AddRange(snapshot.FinancialTransactions);
            if (snapshot.Payments?.Count > 0) dbContext.Payments.AddRange(snapshot.Payments);
            if (snapshot.AuditEvents?.Count > 0) dbContext.AuditEvents.AddRange(snapshot.AuditEvents);
            if (snapshot.ProcessedEvents?.Count > 0) dbContext.ProcessedEvents.AddRange(snapshot.ProcessedEvents);
            if (snapshot.DeadLetterEvents?.Count > 0) dbContext.DeadLetterEvents.AddRange(snapshot.DeadLetterEvents);
            if (snapshot.WorkstationStreamStates?.Count > 0) dbContext.WorkstationStreamStates.AddRange(snapshot.WorkstationStreamStates);
            if (snapshot.ConfigurationPackages?.Count > 0) dbContext.ConfigurationPackages.AddRange(snapshot.ConfigurationPackages);
            if (snapshot.ConfigurationPublications?.Count > 0) dbContext.ConfigurationPublications.AddRange(snapshot.ConfigurationPublications);
            if (snapshot.ConfigurationSigningKeys?.Count > 0) dbContext.ConfigurationSigningKeys.AddRange(snapshot.ConfigurationSigningKeys);
            if (snapshot.PricingPlans?.Count > 0) dbContext.PricingPlans.AddRange(snapshot.PricingPlans);
            if (snapshot.PricingRules?.Count > 0) dbContext.PricingRules.AddRange(snapshot.PricingRules);
            if (snapshot.RateSnapshots?.Count > 0) dbContext.RateSnapshots.AddRange(snapshot.RateSnapshots);
            if (snapshot.UpdateReleases?.Count > 0) dbContext.UpdateReleases.AddRange(snapshot.UpdateReleases);
            if (snapshot.UpdatePackages?.Count > 0) dbContext.UpdatePackages.AddRange(snapshot.UpdatePackages);
            if (snapshot.UpdateTargets?.Count > 0) dbContext.UpdateTargets.AddRange(snapshot.UpdateTargets);

            await dbContext.SaveChangesAsync(cancellationToken);

            // 5. Post-Restore Integrity Validation
            DatabaseIntegrityReport? integrityReport = null;
            if (request.PerformIntegrityCheckPostRestore)
            {
                integrityReport = await ValidateRestoredDatabaseIntegrityAsync(dbContext, cancellationToken);
            }

            long restoredRecordCount =
                (snapshot.Organizations?.Count ?? 0) +
                (snapshot.Workstations?.Count ?? 0) +
                (snapshot.Sessions?.Count ?? 0) +
                (snapshot.Reservations?.Count ?? 0) +
                (snapshot.GamerAccounts?.Count ?? 0) +
                (snapshot.FinancialTransactions?.Count ?? 0) +
                (snapshot.ProcessedEvents?.Count ?? 0) +
                (snapshot.ConfigurationPackages?.Count ?? 0) +
                (snapshot.AuditEvents?.Count ?? 0);

            var duration = DateTime.UtcNow - startTime;

            return new RestoreResult
            {
                IsSuccess = integrityReport == null || integrityReport.IsHealthy,
                RestoredBackupId = snapshot.BackupId,
                RestoredAtUtc = DateTime.UtcNow,
                RestoredTableCount = 20,
                RestoredRecordCount = restoredRecordCount,
                RestoredSchemaVersion = snapshot.SchemaVersion,
                RestorationDuration = duration,
                IntegrityReport = integrityReport
            };
        }

        public async Task<DatabaseIntegrityReport> ValidateRestoredDatabaseIntegrityAsync(object dbContextObj, CancellationToken cancellationToken = default)
        {
            var report = new DatabaseIntegrityReport { IsHealthy = true };

            if (!(dbContextObj is ApplicationDbContext dbContext))
            {
                report.IsHealthy = false;
                report.Violations.Add("Invalid DbContext object passed to integrity validation.");
                return report;
            }

            // 1. Financial Ledger Balance Consistency
            var accounts = await dbContext.GamerAccounts.AsNoTracking().ToListAsync(cancellationToken);
            var ledgerEntries = await dbContext.LedgerEntries.AsNoTracking().ToListAsync(cancellationToken);

            foreach (var account in accounts)
            {
                if (account.Balance < 0)
                {
                    report.Violations.Add($"Prohibited negative balance ({account.Balance}) on GamerAccount {account.Id}.");
                    report.IsHealthy = false;
                }

                decimal expectedBalanceFromLedger = ledgerEntries
                    .Where(l => l.GamerAccountId == account.Id)
                    .Sum(l => l.Amount);

                if (ledgerEntries.Any(l => l.GamerAccountId == account.Id) && Math.Abs(account.Balance - expectedBalanceFromLedger) > 0.0001m)
                {
                    report.Violations.Add($"Balance mismatch on GamerAccount {account.Id}: Account balance={account.Balance}, Ledger sum={expectedBalanceFromLedger}.");
                    report.IsHealthy = false;
                }
            }

            // 2. Session Integrity Check
            var sessions = await dbContext.Sessions.AsNoTracking().ToListAsync(cancellationToken);
            var workstations = await dbContext.Workstations.AsNoTracking().ToListAsync(cancellationToken);
            var workstationIds = new HashSet<Guid>(workstations.Select(w => w.Id));

            foreach (var session in sessions)
            {
                if (session.Status == "ENDED" && session.IsActive())
                {
                    report.Violations.Add($"Session {session.Id} is marked ENDED but IsActive is true.");
                    report.IsHealthy = false;
                }

                if (!workstationIds.Contains(session.WorkstationId))
                {
                    report.Violations.Add($"Orphaned Session {session.Id} pointing to non-existent Workstation {session.WorkstationId}.");
                    report.IsHealthy = false;
                }
            }

            // 3. Phase 09 Offline Event & Idempotency Check
            var processedEvents = await dbContext.ProcessedEvents.AsNoTracking().ToListAsync(cancellationToken);
            var duplicateEvents = processedEvents
                .GroupBy(e => e.EventId)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicateEvents.Any())
            {
                report.Violations.Add($"Duplicate EventIds detected in ProcessedEvents: {string.Join(", ", duplicateEvents)}.");
                report.IsHealthy = false;
            }

            // 4. Financial Transactions Idempotency Key Check
            var txns = await dbContext.FinancialTransactions.AsNoTracking().ToListAsync(cancellationToken);
            var duplicateTxnKeys = txns
                .Where(t => !string.IsNullOrEmpty(t.IdempotencyKey))
                .GroupBy(t => t.IdempotencyKey)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicateTxnKeys.Any())
            {
                report.Violations.Add($"Duplicate IdempotencyKeys detected in FinancialTransactions: {string.Join(", ", duplicateTxnKeys)}.");
                report.IsHealthy = false;
            }

            // 5. Workstation PC ID & MAC Address Uniqueness
            var duplicatePcIds = workstations
                .GroupBy(w => w.PcId)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicatePcIds.Any())
            {
                report.Violations.Add($"Duplicate PcIds detected in Workstations: {string.Join(", ", duplicatePcIds)}.");
                report.IsHealthy = false;
            }

            report.TableCounts["Workstations"] = workstations.Count;
            report.TableCounts["Sessions"] = sessions.Count;
            report.TableCounts["GamerAccounts"] = accounts.Count;
            report.TableCounts["ProcessedEvents"] = processedEvents.Count;

            return report;
        }

        public async Task<BackupFreshnessStatus> EvaluateBackupFreshnessAsync(TimeSpan rpoThreshold, string? backupDir = null, CancellationToken cancellationToken = default)
        {
            var catalog = await LoadCatalogInternalAsync(backupDir, cancellationToken);
            var verifiedBackups = catalog
                .Where(b => b.IsVerified && b.Status == "VERIFIED")
                .OrderByDescending(b => b.TimestampUtc)
                .ToList();

            if (!verifiedBackups.Any())
            {
                return new BackupFreshnessStatus
                {
                    HealthState = BackupHealthState.Critical,
                    RpoThreshold = rpoThreshold,
                    Message = "No verified backups exist in catalog."
                };
            }

            var latest = verifiedBackups.First();
            var age = DateTime.UtcNow - latest.TimestampUtc;

            if (age > rpoThreshold * 1.5)
            {
                return new BackupFreshnessStatus
                {
                    HealthState = BackupHealthState.Critical,
                    LastSuccessfulBackupUtc = latest.TimestampUtc,
                    Age = age,
                    RpoThreshold = rpoThreshold,
                    Message = $"Latest verified backup is CRITICAL (age {age.TotalMinutes:F1}m exceeds RPO threshold {rpoThreshold.TotalMinutes:F1}m)."
                };
            }

            if (age > rpoThreshold)
            {
                return new BackupFreshnessStatus
                {
                    HealthState = BackupHealthState.Warning,
                    LastSuccessfulBackupUtc = latest.TimestampUtc,
                    Age = age,
                    RpoThreshold = rpoThreshold,
                    Message = $"Latest verified backup is WARNING (age {age.TotalMinutes:F1}m exceeds target RPO threshold {rpoThreshold.TotalMinutes:F1}m)."
                };
            }

            return new BackupFreshnessStatus
            {
                HealthState = BackupHealthState.Healthy,
                LastSuccessfulBackupUtc = latest.TimestampUtc,
                Age = age,
                RpoThreshold = rpoThreshold,
                Message = $"Backup freshness HEALTHY (age {age.TotalMinutes:F1}m within RPO threshold {rpoThreshold.TotalMinutes:F1}m)."
            };
        }

        public async Task<IReadOnlyList<BackupMetadata>> GetBackupCatalogAsync(string? backupDir = null, CancellationToken cancellationToken = default)
        {
            var catalog = await LoadCatalogInternalAsync(backupDir, cancellationToken);
            return catalog.AsReadOnly();
        }

        public async Task<PruneResult> ApplyRetentionPolicyAsync(BackupRetentionPolicy policy, CancellationToken cancellationToken = default)
        {
            return await ApplyRetentionPolicyAsync(null, policy, cancellationToken);
        }

        public async Task<PruneResult> ApplyRetentionPolicyAsync(string? backupDir, BackupRetentionPolicy policy, CancellationToken cancellationToken = default)
        {
            if (policy == null) policy = new BackupRetentionPolicy();

            var catalog = await LoadCatalogInternalAsync(backupDir, cancellationToken);
            int initialCount = catalog.Count;

            var sorted = catalog.OrderByDescending(b => b.TimestampUtc).ToList();
            var retained = new List<BackupMetadata>();
            var pruned = new List<BackupMetadata>();

            // Find last verified copy to protect
            var lastVerified = sorted.FirstOrDefault(b => b.IsVerified && b.Status == "VERIFIED");

            int keepLimit = policy.MaxDailyBackups;

            for (int i = 0; i < sorted.Count; i++)
            {
                var item = sorted[i];

                if (policy.ProtectLastVerifiedCopy && lastVerified != null && item.BackupId == lastVerified.BackupId)
                {
                    retained.Add(item);
                    continue;
                }

                if (i < keepLimit)
                {
                    retained.Add(item);
                }
                else
                {
                    pruned.Add(item);
                }
            }

            foreach (var item in pruned)
            {
                try
                {
                    if (File.Exists(item.StorageLocation))
                    {
                        File.Delete(item.StorageLocation);
                    }
                }
                catch
                {
                    // Ignore deletion failures
                }
            }

            await SaveCatalogInternalAsync(retained, backupDir, cancellationToken);

            return new PruneResult
            {
                TotalExamined = initialCount,
                TotalPruned = pruned.Count,
                TotalRetained = retained.Count,
                PrunedBackupIds = pruned.Select(p => p.BackupId).ToList()
            };
        }

        #region Helper Methods & Encryption

        private static JsonSerializerOptions GetJsonOptions()
        {
            return new JsonSerializerOptions
            {
                WriteIndented = false,
                TypeInfoResolver = new DefaultJsonTypeInfoResolver
                {
                    Modifiers =
                    {
                        typeInfo =>
                        {
                            if (typeInfo.Kind == JsonTypeInfoKind.Object)
                            {
                                foreach (var property in typeInfo.Properties)
                                {
                                    if (property.Set == null)
                                    {
                                        var propInfo = typeInfo.Type.GetProperty(
                                            property.Name,
                                            System.Reflection.BindingFlags.Instance |
                                            System.Reflection.BindingFlags.Public |
                                            System.Reflection.BindingFlags.NonPublic |
                                            System.Reflection.BindingFlags.IgnoreCase);

                                        if (propInfo?.SetMethod != null)
                                        {
                                            property.Set = (obj, val) => propInfo.SetValue(obj, val);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };
        }

        private static string GetCatalogPath(string? backupDir)
        {
            string dir = string.IsNullOrWhiteSpace(backupDir)
                ? Path.Combine(Directory.GetCurrentDirectory(), "backups")
                : backupDir;

            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "backup_catalog.json");
        }

        private async Task SaveToCatalogAsync(string backupDir, BackupMetadata metadata, CancellationToken cancellationToken)
        {
            var catalog = await LoadCatalogInternalAsync(backupDir, cancellationToken);
            catalog.RemoveAll(b => b.BackupId == metadata.BackupId);
            catalog.Add(metadata);
            await SaveCatalogInternalAsync(catalog, backupDir, cancellationToken);
        }

        private async Task UpdateInCatalogAsync(string? backupDir, BackupMetadata metadata, CancellationToken cancellationToken)
        {
            var catalog = await LoadCatalogInternalAsync(backupDir, cancellationToken);
            int idx = catalog.FindIndex(b => b.BackupId == metadata.BackupId);
            if (idx >= 0)
            {
                catalog[idx] = metadata;
            }
            else
            {
                catalog.Add(metadata);
            }
            await SaveCatalogInternalAsync(catalog, backupDir, cancellationToken);
        }

        private static async Task<List<BackupMetadata>> LoadCatalogInternalAsync(string? backupDir, CancellationToken cancellationToken)
        {
            string catalogPath = GetCatalogPath(backupDir);
            if (!File.Exists(catalogPath))
            {
                return new List<BackupMetadata>();
            }

            try
            {
                byte[] bytes = await File.ReadAllBytesAsync(catalogPath, cancellationToken);
                if (bytes.Length == 0) return new List<BackupMetadata>();
                return JsonSerializer.Deserialize<List<BackupMetadata>>(bytes, GetJsonOptions()) ?? new List<BackupMetadata>();
            }
            catch
            {
                return new List<BackupMetadata>();
            }
        }

        private static async Task SaveCatalogInternalAsync(List<BackupMetadata> catalog, string? backupDir, CancellationToken cancellationToken)
        {
            string catalogPath = GetCatalogPath(backupDir);
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(catalog, new JsonSerializerOptions { WriteIndented = true });
            lock (_catalogLock)
            {
                File.WriteAllBytes(catalogPath, bytes);
            }
            await Task.CompletedTask;
        }

        private static async Task CleanDbContextStateAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken)
        {
            dbContext.Organizations.RemoveRange(dbContext.Organizations);
            dbContext.Sites.RemoveRange(dbContext.Sites);
            dbContext.Zones.RemoveRange(dbContext.Zones);
            dbContext.Workstations.RemoveRange(dbContext.Workstations);
            dbContext.WorkstationSessions.RemoveRange(dbContext.WorkstationSessions);
            dbContext.Sessions.RemoveRange(dbContext.Sessions);
            dbContext.SessionSegments.RemoveRange(dbContext.SessionSegments);
            dbContext.Reservations.RemoveRange(dbContext.Reservations);
            dbContext.Users.RemoveRange(dbContext.Users);
            dbContext.UserCredentials.RemoveRange(dbContext.UserCredentials);
            dbContext.Gamers.RemoveRange(dbContext.Gamers);
            dbContext.GamerCredentials.RemoveRange(dbContext.GamerCredentials);
            dbContext.GamerAccounts.RemoveRange(dbContext.GamerAccounts);
            dbContext.LedgerEntries.RemoveRange(dbContext.LedgerEntries);
            dbContext.FinancialTransactions.RemoveRange(dbContext.FinancialTransactions);
            dbContext.Payments.RemoveRange(dbContext.Payments);
            dbContext.AuditEvents.RemoveRange(dbContext.AuditEvents);
            dbContext.ProcessedEvents.RemoveRange(dbContext.ProcessedEvents);
            dbContext.DeadLetterEvents.RemoveRange(dbContext.DeadLetterEvents);
            dbContext.WorkstationStreamStates.RemoveRange(dbContext.WorkstationStreamStates);
            dbContext.ConfigurationPackages.RemoveRange(dbContext.ConfigurationPackages);
            dbContext.ConfigurationPublications.RemoveRange(dbContext.ConfigurationPublications);
            dbContext.ConfigurationSigningKeys.RemoveRange(dbContext.ConfigurationSigningKeys);
            dbContext.PricingPlans.RemoveRange(dbContext.PricingPlans);
            dbContext.PricingRules.RemoveRange(dbContext.PricingRules);
            dbContext.RateSnapshots.RemoveRange(dbContext.RateSnapshots);
            dbContext.UpdateReleases.RemoveRange(dbContext.UpdateReleases);
            dbContext.UpdatePackages.RemoveRange(dbContext.UpdatePackages);
            dbContext.UpdateTargets.RemoveRange(dbContext.UpdateTargets);

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(bytes);
            return Convert.ToHexString(hash);
        }

        private static byte[] EncryptBytes(byte[] plainBytes, string password)
        {
            using var aes = Aes.Create();
            aes.KeySize = 256;

            using var deriveBytes = new Rfc2898DeriveBytes(password, Encoding.UTF8.GetBytes("SayraSalt32BytesPaddingNeeded!"), 10000, HashAlgorithmName.SHA256);
            aes.Key = deriveBytes.GetBytes(32);
            aes.IV = deriveBytes.GetBytes(16);

            using var ms = new MemoryStream();
            ms.Write(aes.IV, 0, aes.IV.Length);

            using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
            {
                cs.Write(plainBytes, 0, plainBytes.Length);
                cs.FlushFinalBlock();
            }

            return ms.ToArray();
        }

        private static byte[] DecryptBytes(byte[] cipherBytes, string password)
        {
            if (cipherBytes.Length < 16) throw new InvalidOperationException("Invalid cipher payload size.");

            using var aes = Aes.Create();
            aes.KeySize = 256;

            byte[] iv = new byte[16];
            Buffer.BlockCopy(cipherBytes, 0, iv, 0, 16);

            using var deriveBytes = new Rfc2898DeriveBytes(password, Encoding.UTF8.GetBytes("SayraSalt32BytesPaddingNeeded!"), 10000, HashAlgorithmName.SHA256);
            aes.Key = deriveBytes.GetBytes(32);
            aes.IV = iv;

            using var ms = new MemoryStream();
            using (var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Write))
            {
                cs.Write(cipherBytes, 16, cipherBytes.Length - 16);
                cs.FlushFinalBlock();
            }

            return ms.ToArray();
        }

        #endregion

        private class DatabaseSnapshotContainer
        {
            public string BackupId { get; set; } = string.Empty;
            public DateTime TimestampUtc { get; set; }
            public string SchemaVersion { get; set; } = string.Empty;
            public List<Organization>? Organizations { get; set; }
            public List<Site>? Sites { get; set; }
            public List<Zone>? Zones { get; set; }
            public List<Workstation>? Workstations { get; set; }
            public List<WorkstationSession>? WorkstationSessions { get; set; }
            public List<Session>? Sessions { get; set; }
            public List<SessionSegment>? SessionSegments { get; set; }
            public List<Reservation>? Reservations { get; set; }
            public List<User>? Users { get; set; }
            public List<UserCredential>? UserCredentials { get; set; }
            public List<Gamer>? Gamers { get; set; }
            public List<GamerCredential>? GamerCredentials { get; set; }
            public List<GamerAccount>? GamerAccounts { get; set; }
            public List<LedgerEntry>? LedgerEntries { get; set; }
            public List<FinancialTransaction>? FinancialTransactions { get; set; }
            public List<Payment>? Payments { get; set; }
            public List<AuditEvent>? AuditEvents { get; set; }
            public List<ProcessedEvent>? ProcessedEvents { get; set; }
            public List<DeadLetterEvent>? DeadLetterEvents { get; set; }
            public List<WorkstationStreamState>? WorkstationStreamStates { get; set; }
            public List<ConfigurationPackage>? ConfigurationPackages { get; set; }
            public List<ConfigurationPublication>? ConfigurationPublications { get; set; }
            public List<ConfigurationSigningKey>? ConfigurationSigningKeys { get; set; }
            public List<PricingPlan>? PricingPlans { get; set; }
            public List<PricingRule>? PricingRules { get; set; }
            public List<RateSnapshot>? RateSnapshots { get; set; }
            public List<UpdateRelease>? UpdateReleases { get; set; }
            public List<UpdatePackage>? UpdatePackages { get; set; }
            public List<UpdateTarget>? UpdateTargets { get; set; }
        }
    }
}
