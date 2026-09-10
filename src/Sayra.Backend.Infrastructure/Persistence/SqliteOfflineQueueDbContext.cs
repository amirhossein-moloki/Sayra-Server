using Microsoft.EntityFrameworkCore;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Infrastructure.Persistence
{
    public class SqliteOfflineQueueDbContext : DbContext
    {
        public DbSet<DurableQueueItemEntity> QueueItems => Set<DurableQueueItemEntity>();

        public SqliteOfflineQueueDbContext(DbContextOptions<SqliteOfflineQueueDbContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<DurableQueueItemEntity>(entity =>
            {
                entity.ToTable("OfflineEventQueue");

                entity.HasKey(e => e.Id);

                entity.HasIndex(e => e.EventId)
                      .IsUnique();

                entity.HasIndex(e => new { e.ReliabilityClass, e.SequenceNumber, e.OccurredAt });

                entity.HasIndex(e => e.Status);

                entity.HasIndex(e => e.OccurredAt);

                entity.Property(e => e.EventId).IsRequired().HasMaxLength(64);
                entity.Property(e => e.EventType).IsRequired().HasMaxLength(128);
                entity.Property(e => e.ClientId).IsRequired().HasMaxLength(128);
                entity.Property(e => e.WorkstationId).IsRequired().HasMaxLength(128);
                entity.Property(e => e.ContractVersion).HasMaxLength(16);
                entity.Property(e => e.ReliabilityClass).HasMaxLength(32);
                entity.Property(e => e.Status).HasMaxLength(32);
                entity.Property(e => e.Payload).HasColumnType("TEXT");
            });
        }
    }
}
