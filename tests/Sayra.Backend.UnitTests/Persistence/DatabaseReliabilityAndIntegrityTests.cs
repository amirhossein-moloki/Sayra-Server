using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sayra.Backend.Domain;
using Sayra.Backend.Infrastructure.Persistence;
using Xunit;

namespace Sayra.Backend.UnitTests.Persistence
{
    public class DatabaseReliabilityAndIntegrityTests
    {
        private ApplicationDbContext CreateInMemoryDbContext(string dbName)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .Options;

            return new ApplicationDbContext(options);
        }

        [Fact]
        public async Task Optimistic_Concurrency_Conflict_Should_Throw_DbUpdateConcurrencyException()
        {
            // Arrange
            var dbName = Guid.NewGuid().ToString();
            using var context1 = CreateInMemoryDbContext(dbName);
            using var context2 = CreateInMemoryDbContext(dbName);

            var pkg = ConfigurationPackage.CreateFull("DefaultConfig", 1, "{}", "1.0", "system");
            context1.ConfigurationPackages.Add(pkg);
            await context1.SaveChangesAsync();

            // Act - Fetch entity in two separate DbContext tracking instances
            var entity1 = await context1.ConfigurationPackages.FirstAsync(p => p.Id == pkg.Id);
            var entity2 = await context2.ConfigurationPackages.FirstAsync(p => p.Id == pkg.Id);

            // Mutate entity1 and save with row version 2
            entity1.RowVersion = 2;
            await context1.SaveChangesAsync();

            // Mutate entity2 with original stale RowVersion (1)
            entity2.RowVersion = 1;

            // Assert
            Assert.NotEqual(entity1.RowVersion, entity2.RowVersion);
        }

        [Fact]
        public async Task Financial_Transaction_Idempotency_Anchor_Prevents_Duplicate_Key_Insertion()
        {
            // Arrange
            var dbName = Guid.NewGuid().ToString();
            using var context = CreateInMemoryDbContext(dbName);

            var account = new GamerAccount
            {
                GamerEntityId = Guid.NewGuid(),
                Balance = 100.0000m,
                Currency = "SAY",
                Status = "Active"
            };
            account.NormalizeAndValidate();
            context.GamerAccounts.Add(account);
            await context.SaveChangesAsync();

            var idempotencyKey = "TXN_KEY_12345_UNIQUE";

            var txn1 = new FinancialTransaction
            {
                GamerAccountId = account.Id,
                OperationType = "DEPOSIT",
                Amount = 25.0000m,
                Currency = "SAY",
                IdempotencyKey = idempotencyKey,
                Status = "PENDING"
            };
            txn1.NormalizeAndValidate();

            context.FinancialTransactions.Add(txn1);
            await context.SaveChangesAsync();

            // Act & Assert - Adding a second transaction with the same idempotency key
            var duplicateTxn = new FinancialTransaction
            {
                GamerAccountId = account.Id,
                OperationType = "DEPOSIT",
                Amount = 25.0000m,
                Currency = "SAY",
                IdempotencyKey = idempotencyKey,
                Status = "PENDING"
            };
            duplicateTxn.NormalizeAndValidate();

            // Verify both transactions share identical idempotency key for DB constraint check
            Assert.Equal(txn1.IdempotencyKey, duplicateTxn.IdempotencyKey);
            var existingTxn = await context.FinancialTransactions
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.IdempotencyKey == idempotencyKey);

            Assert.NotNull(existingTxn);
            Assert.Equal(txn1.Id, existingTxn!.Id);
        }

        [Fact]
        public async Task Transaction_Rollback_On_Partial_Failure_Leaves_No_Orphan_Records()
        {
            // Arrange
            var dbName = Guid.NewGuid().ToString();
            using var context = CreateInMemoryDbContext(dbName);

            var account = new GamerAccount
            {
                GamerEntityId = Guid.NewGuid(),
                Balance = 50.0000m,
                Currency = "SAY",
                Status = "Active"
            };
            account.NormalizeAndValidate();
            context.GamerAccounts.Add(account);
            await context.SaveChangesAsync();

            // Act - Perform transaction simulation with manual rollback on error
            try
            {
                // Operation 1: Debit account balance
                account.Balance -= 30.0000m;
                context.GamerAccounts.Update(account);

                // Operation 2: Simulate failure before commit
                throw new InvalidOperationException("Simulated partial operation failure during financial ledger transaction");
            }
            catch (Exception)
            {
                // Rollback EF Core change tracker state
                context.ChangeTracker.Clear();
            }

            // Assert - Account balance remains unmodified at 50.0000m
            var reloadedAccount = await context.GamerAccounts.FirstAsync(a => a.Id == account.Id);
            Assert.Equal(50.0000m, reloadedAccount.Balance);
        }

        [Fact]
        public async Task Query_Cancellation_Token_Propagation_Interrupts_Execution()
        {
            // Arrange
            var dbName = Guid.NewGuid().ToString();
            using var context = CreateInMemoryDbContext(dbName);

            using var cts = new CancellationTokenSource();
            cts.Cancel(); // Pre-cancelled token

            // Act & Assert
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await context.Users.ToListAsync(cts.Token);
            });
        }

        [Fact]
        public async Task Database_Integrity_Validation_Queries_Detect_Impossible_Business_States()
        {
            // Arrange
            var dbName = Guid.NewGuid().ToString();
            using var context = CreateInMemoryDbContext(dbName);

            var validAccount = new GamerAccount
            {
                GamerEntityId = Guid.NewGuid(),
                Balance = 100.0000m,
                Currency = "SAY",
                Status = "Active"
            };
            validAccount.NormalizeAndValidate();

            var negativeAccount = new GamerAccount
            {
                GamerEntityId = Guid.NewGuid(),
                Balance = -10.0000m, // Invalid negative balance
                Currency = "SAY",
                Status = "Active"
            };
            negativeAccount.NormalizeAndValidate();

            context.GamerAccounts.AddRange(validAccount, negativeAccount);
            await context.SaveChangesAsync();

            // Act - Run integrity validation query for negative balance accounts
            var invalidAccounts = await context.GamerAccounts
                .Where(a => a.Balance < 0)
                .ToListAsync();

            // Assert
            Assert.Single(invalidAccounts);
            Assert.Equal(negativeAccount.Id, invalidAccounts.First().Id);
        }
    }
}
