using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Infrastructure.Persistence
{
    public class ApplicationDbContext : DbContext, IUnitOfWork
    {
        private IDbContextTransaction? _currentTransaction;

        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Organization> Organizations { get; set; } = null!;
        public DbSet<Site> Sites { get; set; } = null!;
        public DbSet<Zone> Zones { get; set; } = null!;
        public DbSet<Workstation> Workstations { get; set; } = null!;
        public DbSet<WorkstationSession> WorkstationSessions { get; set; } = null!;
        public DbSet<Session> Sessions { get; set; } = null!;
        public DbSet<SessionSegment> SessionSegments { get; set; } = null!;
        public DbSet<Reservation> Reservations { get; set; } = null!;
        public DbSet<User> Users { get; set; } = null!;
        public DbSet<UserCredential> UserCredentials { get; set; } = null!;
        public DbSet<Gamer> Gamers { get; set; } = null!;
        public DbSet<GamerCredential> GamerCredentials { get; set; } = null!;
        public DbSet<GamerAccount> GamerAccounts { get; set; } = null!;
        public DbSet<LedgerEntry> LedgerEntries { get; set; } = null!;
        public DbSet<FinancialTransaction> FinancialTransactions { get; set; } = null!;
        public DbSet<Payment> Payments { get; set; } = null!;
        public DbSet<AuditEvent> AuditEvents { get; set; } = null!;
        public DbSet<TelemetryMetric> TelemetryMetrics { get; set; } = null!;
        public DbSet<ConfigurationPackage> ConfigurationPackages { get; set; } = null!;
        public DbSet<ConfigurationPublication> ConfigurationPublications { get; set; } = null!;
        public DbSet<ConfigurationSigningKey> ConfigurationSigningKeys { get; set; } = null!;
        public DbSet<SystemUpdate> SystemUpdates { get; set; } = null!;
        public DbSet<PricingPlan> PricingPlans { get; set; } = null!;
        public DbSet<PricingRule> PricingRules { get; set; } = null!;
        public DbSet<RateSnapshot> RateSnapshots { get; set; } = null!;
        public DbSet<SessionExtension> SessionExtensions { get; set; } = null!;
        public DbSet<Role> Roles { get; set; } = null!;
        public DbSet<Permission> Permissions { get; set; } = null!;
        public DbSet<UserRoleEntity> UserRoles { get; set; } = null!;
        public DbSet<RolePermission> RolePermissions { get; set; } = null!;
        public DbSet<UserResourceAccess> UserResourceAccesses { get; set; } = null!;
        public DbSet<SecurityEvent> SecurityEvents { get; set; } = null!;
        public DbSet<LoginAttempt> LoginAttempts { get; set; } = null!;
        public DbSet<CommunicationSession> CommunicationSessions { get; set; } = null!;
        public DbSet<RemoteCommand> RemoteCommands { get; set; } = null!;
        public DbSet<WorkstationGroup> WorkstationGroups { get; set; } = null!;
        public DbSet<WorkstationGroupMember> WorkstationGroupMembers { get; set; } = null!;
        public DbSet<ConfigurationTarget> ConfigurationTargets { get; set; } = null!;
        public DbSet<ConfigurationAssignment> ConfigurationAssignments { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            // Apply configurations from assembly for modular entity registrations
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        }

        public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            if (_currentTransaction != null)
            {
                return;
            }

            _currentTransaction = await Database.BeginTransactionAsync(cancellationToken);
        }

        public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await SaveChangesAsync(cancellationToken);
                if (_currentTransaction != null)
                {
                    await _currentTransaction.CommitAsync(cancellationToken);
                }
            }
            catch
            {
                await RollbackTransactionAsync(cancellationToken);
                throw;
            }
            finally
            {
                if (_currentTransaction != null)
                {
                    _currentTransaction.Dispose();
                    _currentTransaction = null;
                }
            }
        }

        public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                if (_currentTransaction != null)
                {
                    await _currentTransaction.RollbackAsync(cancellationToken);
                }
            }
            finally
            {
                if (_currentTransaction != null)
                {
                    _currentTransaction.Dispose();
                    _currentTransaction = null;
                }
            }
        }

        public async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
        {
            if (Database.CurrentTransaction != null)
            {
                return await operation();
            }

            var strategy = Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                if (Database.CurrentTransaction != null)
                {
                    return await operation();
                }

                using var transaction = await Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    var result = await operation();
                    await SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return result;
                }
                catch
                {
                    await transaction.RollbackAsync(cancellationToken);
                    throw;
                }
            });
        }

        public override void Dispose()
        {
            _currentTransaction?.Dispose();
            base.Dispose();
        }

        public override async ValueTask DisposeAsync()
        {
            if (_currentTransaction != null)
            {
                await _currentTransaction.DisposeAsync();
            }
            await base.DisposeAsync();
        }
    }
}
