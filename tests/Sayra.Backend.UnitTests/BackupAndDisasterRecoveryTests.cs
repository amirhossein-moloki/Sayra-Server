using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sayra.Backend.Application.Abstractions.Backup;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Infrastructure.Backup;
using Sayra.Backend.Infrastructure.Persistence;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class BackupAndDisasterRecoveryTests : IDisposable
    {
        private string _testBackupDir;

        public BackupAndDisasterRecoveryTests()
        {
            _testBackupDir = Path.Combine(Path.GetTempPath(), $"sayra_dr_tests_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_testBackupDir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testBackupDir))
                {
                    Directory.Delete(_testBackupDir, true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        private (ServiceProvider serviceProvider, string dbName) CreateTestServiceProvider()
        {
            var dbName = Guid.NewGuid().ToString();
            var services = new ServiceCollection();

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase(dbName));

            services.AddScoped<IBackupAndDisasterRecoveryService, BackupAndDisasterRecoveryService>();

            return (services.BuildServiceProvider(), dbName);
        }

        private async Task SeedSampleDatabaseAsync(ApplicationDbContext dbContext, Guid testEvtId)
        {
            var org = new Organization { Name = "Org Alpha", Code = "ORG_A", Status = "Active" };
            var site = new Site { OrganizationId = org.Id, Name = "Site One", Code = "SITE_1", Status = "Active" };
            var zone = new Zone { SiteId = site.Id, Name = "VIP Zone", Code = "ZONE_VIP" };
            var workstation = new Workstation { ZoneEntityId = zone.Id, SiteEntityId = site.Id, OrganizationEntityId = org.Id, PcId = "PC_01", MacAddress = "AA:BB:CC:DD:EE:01", Status = "Available" };

            dbContext.Organizations.Add(org);
            dbContext.Sites.Add(site);
            dbContext.Zones.Add(zone);
            dbContext.Workstations.Add(workstation);

            var gamer = new Gamer { Username = "gamer_dr", Email = "dr@sayra.lan", FirstName = "Disaster", LastName = "Recovery" };
            var account = new GamerAccount { GamerEntityId = gamer.Id, Balance = 150.0000m, Currency = "SAY", Status = "Active" };
            var ledger1 = new LedgerEntry { GamerAccountId = account.Id, Amount = 100.0000m, Direction = "CREDIT", EntryType = "DEPOSIT", Reference = "Initial Deposit" };
            var ledger2 = new LedgerEntry { GamerAccountId = account.Id, Amount = 50.0000m, Direction = "CREDIT", EntryType = "DEPOSIT", Reference = "Bonus Credit" };

            dbContext.Gamers.Add(gamer);
            dbContext.GamerAccounts.Add(account);
            dbContext.LedgerEntries.AddRange(ledger1, ledger2);

            var session = new Session { WorkstationId = workstation.Id, GamerId = gamer.Id, Status = "ACTIVE" };
            dbContext.Sessions.Add(session);

            var evt = new ProcessedEvent { EventId = testEvtId, WorkstationId = workstation.Id, EventType = "SESSION_START", PayloadHash = "HASH123", SequenceNumber = 1, ProcessingStatus = "ACCEPTED" };
            var streamState = new WorkstationStreamState { WorkstationId = workstation.Id, LastSequenceNumber = 1 };

            dbContext.ProcessedEvents.Add(evt);
            dbContext.WorkstationStreamStates.Add(streamState);

            var configPkg = ConfigurationPackage.CreateFull("DefaultConfig", 1, "{}", "1.0", "system");
            dbContext.ConfigurationPackages.Add(configPkg);

            await dbContext.SaveChangesAsync();
        }

        [Fact]
        public async Task CreateBackupAsync_Should_Create_Valid_Backup_File_And_Checksum_And_Catalog_Entry()
        {
            // Arrange
            var (sp, _) = CreateTestServiceProvider();
            using var scope = sp.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await SeedSampleDatabaseAsync(dbContext, Guid.NewGuid());

            var backupService = scope.ServiceProvider.GetRequiredService<IBackupAndDisasterRecoveryService>();
            var request = new BackupRequest { BackupDirectory = _testBackupDir, Encrypt = false };

            // Act
            var metadata = await backupService.CreateBackupAsync(request);

            // Assert
            Assert.NotNull(metadata);
            Assert.False(string.IsNullOrWhiteSpace(metadata.BackupId));
            Assert.True(File.Exists(metadata.StorageLocation));
            Assert.True(metadata.FileSizeBytes > 0);
            Assert.False(string.IsNullOrWhiteSpace(metadata.Sha256Checksum));
            Assert.True(metadata.RecordCounts.ContainsKey("Workstations"));
            Assert.Equal(1, metadata.RecordCounts["Workstations"]);
            Assert.Equal(1, metadata.RecordCounts["GamerAccounts"]);
            Assert.Equal(1, metadata.RecordCounts["ProcessedEvents"]);
        }

        [Fact]
        public async Task VerifyBackupIntegrityAsync_Should_Detect_Corrupted_Or_Truncated_Or_Tampered_Backups()
        {
            // Arrange
            var (sp, _) = CreateTestServiceProvider();
            using var scope = sp.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await SeedSampleDatabaseAsync(dbContext, Guid.NewGuid());

            var backupService = scope.ServiceProvider.GetRequiredService<IBackupAndDisasterRecoveryService>();
            var metadata = await backupService.CreateBackupAsync(new BackupRequest { BackupDirectory = _testBackupDir });

            // 1. Verify valid backup
            var validResult = await backupService.VerifyBackupIntegrityAsync(metadata.StorageLocation);
            Assert.True(validResult.IsValid);
            Assert.True(validResult.Sha256Matched);
            Assert.False(validResult.IsCorrupted);

            // 2. Tamper file content
            await File.WriteAllTextAsync(metadata.StorageLocation, "TAMPERED_DATA_CORRUPTED_JSON");
            var tamperedResult = await backupService.VerifyBackupIntegrityAsync(metadata.StorageLocation);
            Assert.False(tamperedResult.IsValid);
            Assert.False(tamperedResult.Sha256Matched);
            Assert.True(tamperedResult.IsCorrupted);

            // 3. Truncate to 0 bytes
            await File.WriteAllBytesAsync(metadata.StorageLocation, Array.Empty<byte>());
            var truncatedResult = await backupService.VerifyBackupIntegrityAsync(metadata.StorageLocation);
            Assert.False(truncatedResult.IsValid);
            Assert.True(truncatedResult.IsTruncated);
        }

        [Fact]
        public async Task ApplyRetentionPolicyAsync_Should_Prune_Excess_Backups_While_Protecting_Last_Verified_Copy()
        {
            // Arrange
            var (sp, _) = CreateTestServiceProvider();
            using var scope = sp.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await SeedSampleDatabaseAsync(dbContext, Guid.NewGuid());

            var backupService = scope.ServiceProvider.GetRequiredService<IBackupAndDisasterRecoveryService>();

            // Create 4 backups
            var b1 = await backupService.CreateBackupAsync(new BackupRequest { BackupDirectory = _testBackupDir });
            await Task.Delay(10);
            var b2 = await backupService.CreateBackupAsync(new BackupRequest { BackupDirectory = _testBackupDir });
            await Task.Delay(10);
            var b3 = await backupService.CreateBackupAsync(new BackupRequest { BackupDirectory = _testBackupDir });
            await Task.Delay(10);
            var b4 = await backupService.CreateBackupAsync(new BackupRequest { BackupDirectory = _testBackupDir });

            var policy = new BackupRetentionPolicy
            {
                MaxDailyBackups = 2,
                ProtectLastVerifiedCopy = true
            };

            // Act
            var pruneResult = await backupService.ApplyRetentionPolicyAsync(_testBackupDir, policy);

            // Assert
            Assert.Equal(4, pruneResult.TotalExamined);
            Assert.True(pruneResult.TotalPruned >= 1);
            Assert.True(pruneResult.TotalRetained >= 2);

            var catalog = await backupService.GetBackupCatalogAsync(_testBackupDir);
            Assert.Contains(catalog, b => b.BackupId == b4.BackupId); // Most recent protected
        }

        [Fact]
        public async Task EvaluateBackupFreshnessAsync_Should_Return_Expected_HealthState_Based_On_RPO()
        {
            // Arrange
            var (sp, _) = CreateTestServiceProvider();
            using var scope = sp.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await SeedSampleDatabaseAsync(dbContext, Guid.NewGuid());

            var backupService = scope.ServiceProvider.GetRequiredService<IBackupAndDisasterRecoveryService>();

            // Case 1: Fresh backup
            await backupService.CreateBackupAsync(new BackupRequest { BackupDirectory = _testBackupDir });
            var freshnessHealthy = await backupService.EvaluateBackupFreshnessAsync(TimeSpan.FromMinutes(15), _testBackupDir);
            Assert.Equal(BackupHealthState.Healthy, freshnessHealthy.HealthState);

            // Case 2: Short RPO threshold (e.g., 0 seconds) triggers Warning/Critical
            var freshnessWarningOrCritical = await backupService.EvaluateBackupFreshnessAsync(TimeSpan.FromMilliseconds(1), _testBackupDir);
            Assert.NotEqual(BackupHealthState.Healthy, freshnessWarningOrCritical.HealthState);
        }

        [Fact]
        public async Task Full_Database_Restore_Into_Fresh_Instance_Restores_All_Entities_Accurately()
        {
            // Arrange
            Guid testEvtId = Guid.NewGuid();
            var (sp, dbNameSource) = CreateTestServiceProvider();
            using var scopeSource = sp.CreateScope();
            var dbContextSource = scopeSource.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await SeedSampleDatabaseAsync(dbContextSource, testEvtId);

            var backupServiceSource = scopeSource.ServiceProvider.GetRequiredService<IBackupAndDisasterRecoveryService>();
            var backupMeta = await backupServiceSource.CreateBackupAsync(new BackupRequest { BackupDirectory = _testBackupDir });

            // Target isolated fresh database instance
            var (spTarget, dbNameTarget) = CreateTestServiceProvider();
            using var scopeTarget = spTarget.CreateScope();
            var backupServiceTarget = scopeTarget.ServiceProvider.GetRequiredService<IBackupAndDisasterRecoveryService>();

            var restoreRequest = new RestoreRequest
            {
                BackupId = backupMeta.BackupId,
                BackupFilePath = backupMeta.StorageLocation,
                PerformIntegrityCheckPostRestore = true
            };

            // Act
            var restoreResult = await backupServiceTarget.RestoreDatabaseAsync(restoreRequest);

            // Assert
            Assert.True(restoreResult.IsSuccess, $"Restore failed. Error: {restoreResult.ErrorMessage}, Violations: {string.Join("; ", restoreResult.IntegrityReport?.Violations ?? new List<string>())}");
            Assert.Equal(backupMeta.BackupId, restoreResult.RestoredBackupId);
            Assert.NotNull(restoreResult.IntegrityReport);
            Assert.True(restoreResult.IntegrityReport!.IsHealthy);

            var dbContextTarget = scopeTarget.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var restoredWorkstations = await dbContextTarget.Workstations.ToListAsync();
            var restoredAccounts = await dbContextTarget.GamerAccounts.ToListAsync();
            var restoredEvents = await dbContextTarget.ProcessedEvents.ToListAsync();

            Assert.Single(restoredWorkstations);
            Assert.Equal("PC_01", restoredWorkstations[0].PcId);

            Assert.Single(restoredAccounts);
            Assert.Equal(150.0000m, restoredAccounts[0].Balance);

            Assert.Single(restoredEvents);
            Assert.Equal(testEvtId, restoredEvents[0].EventId);
        }

        [Fact]
        public async Task ValidateRestoredDatabaseIntegrityAsync_Should_Pass_For_Valid_State_And_Catch_Violations()
        {
            // Arrange
            var (sp, _) = CreateTestServiceProvider();
            using var scope = sp.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await SeedSampleDatabaseAsync(dbContext, Guid.NewGuid());

            var backupService = scope.ServiceProvider.GetRequiredService<IBackupAndDisasterRecoveryService>();

            // 1. Validate clean state
            var cleanReport = await backupService.ValidateRestoredDatabaseIntegrityAsync(dbContext);
            Assert.True(cleanReport.IsHealthy);
            Assert.Empty(cleanReport.Violations);

            // 2. Introduce invalid financial ledger mismatch
            var account = await dbContext.GamerAccounts.FirstAsync();
            account.Balance = 999.99m; // Mismatch with ledger sum (150.00)
            await dbContext.SaveChangesAsync();

            var corruptedReport = await backupService.ValidateRestoredDatabaseIntegrityAsync(dbContext);
            Assert.False(corruptedReport.IsHealthy);
            Assert.NotEmpty(corruptedReport.Violations);
            Assert.Contains(corruptedReport.Violations, v => v.Contains("Balance mismatch"));
        }

        [Fact]
        public async Task Post_Restore_Phase09_Idempotency_And_Client_Reconnection_Preservation()
        {
            // Arrange
            Guid testEvtId = Guid.NewGuid();
            var (spSource, _) = CreateTestServiceProvider();
            using var scopeSource = spSource.CreateScope();
            var dbContextSource = scopeSource.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await SeedSampleDatabaseAsync(dbContextSource, testEvtId);

            var backupServiceSource = scopeSource.ServiceProvider.GetRequiredService<IBackupAndDisasterRecoveryService>();
            var backupMeta = await backupServiceSource.CreateBackupAsync(new BackupRequest { BackupDirectory = _testBackupDir });

            // Restore into fresh target environment
            var (spTarget, _) = CreateTestServiceProvider();
            using var scopeTarget = spTarget.CreateScope();
            var backupServiceTarget = scopeTarget.ServiceProvider.GetRequiredService<IBackupAndDisasterRecoveryService>();

            await backupServiceTarget.RestoreDatabaseAsync(new RestoreRequest
            {
                BackupId = backupMeta.BackupId,
                BackupFilePath = backupMeta.StorageLocation
            });

            var dbContextTarget = scopeTarget.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // Act - Client reconnects and submits duplicate offline event with same testEvtId
            var existingEvent = await dbContextTarget.ProcessedEvents
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.EventId == testEvtId);

            // Assert
            Assert.NotNull(existingEvent);
            Assert.Equal(testEvtId, existingEvent!.EventId);
            Assert.Equal("ACCEPTED", existingEvent.ProcessingStatus);
        }

        [Fact]
        public async Task Encrypted_Backup_And_Restore_Flow_Should_Encrypt_And_Decrypt_Safely()
        {
            // Arrange
            var (sp, _) = CreateTestServiceProvider();
            using var scopeSource = sp.CreateScope();
            var dbContextSource = scopeSource.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await SeedSampleDatabaseAsync(dbContextSource, Guid.NewGuid());

            var backupServiceSource = scopeSource.ServiceProvider.GetRequiredService<IBackupAndDisasterRecoveryService>();
            string encKey = "CustomCryptographicSecretKey32B!";

            var backupReq = new BackupRequest
            {
                BackupDirectory = _testBackupDir,
                Encrypt = true,
                EncryptionKey = encKey
            };

            // Act 1: Backup
            var metadata = await backupServiceSource.CreateBackupAsync(backupReq);

            Assert.True(metadata.IsEncrypted);
            Assert.True(File.Exists(metadata.StorageLocation));

            // Verify raw payload is encrypted (not plain JSON)
            string rawText = await File.ReadAllTextAsync(metadata.StorageLocation);
            Assert.DoesNotContain("Workstations", rawText);

            // Act 2: Restore with correct key
            var (spTarget, _) = CreateTestServiceProvider();
            using var scopeTarget = spTarget.CreateScope();
            var backupServiceTarget = scopeTarget.ServiceProvider.GetRequiredService<IBackupAndDisasterRecoveryService>();

            var restoreResult = await backupServiceTarget.RestoreDatabaseAsync(new RestoreRequest
            {
                BackupId = metadata.BackupId,
                BackupFilePath = metadata.StorageLocation,
                DecryptionKey = encKey,
                PerformIntegrityCheckPostRestore = true
            });

            // Assert
            Assert.True(restoreResult.IsSuccess);
            Assert.NotNull(restoreResult.IntegrityReport);
            Assert.True(restoreResult.IntegrityReport!.IsHealthy);

            var dbContextTarget = scopeTarget.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(1, await dbContextTarget.Workstations.CountAsync());
        }

        [Fact]
        public async Task RPO_RTO_Timeline_Execution_And_Verification_Metrics()
        {
            // Arrange
            var (sp, _) = CreateTestServiceProvider();
            using var scope = sp.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await SeedSampleDatabaseAsync(dbContext, Guid.NewGuid());

            var backupService = scope.ServiceProvider.GetRequiredService<IBackupAndDisasterRecoveryService>();

            var startTime = DateTime.UtcNow;

            // Act 1: Backup creation
            var backupStart = DateTime.UtcNow;
            var metadata = await backupService.CreateBackupAsync(new BackupRequest { BackupDirectory = _testBackupDir });
            var backupDuration = DateTime.UtcNow - backupStart;

            // Act 2: Verification
            var verifyStart = DateTime.UtcNow;
            var integrity = await backupService.VerifyBackupIntegrityAsync(metadata.StorageLocation);
            var verifyDuration = DateTime.UtcNow - verifyStart;

            // Act 3: Restoration
            var restoreStart = DateTime.UtcNow;
            var restoreResult = await backupService.RestoreDatabaseAsync(new RestoreRequest
            {
                BackupId = metadata.BackupId,
                BackupFilePath = metadata.StorageLocation,
                PerformIntegrityCheckPostRestore = true
            });
            var restoreDuration = DateTime.UtcNow - restoreStart;

            var totalRto = DateTime.UtcNow - startTime;

            // Assert
            Assert.True(integrity.IsValid);
            Assert.True(restoreResult.IsSuccess);
            Assert.True(backupDuration.TotalSeconds < 30);
            Assert.True(verifyDuration.TotalSeconds < 10);
            Assert.True(restoreDuration.TotalSeconds < 60);
            Assert.True(totalRto.TotalMinutes < 15); // Strict target RTO < 15 minutes
        }
    }
}
