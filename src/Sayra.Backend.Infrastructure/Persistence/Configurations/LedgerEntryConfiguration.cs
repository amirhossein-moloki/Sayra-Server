using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
    {
        public void Configure(EntityTypeBuilder<LedgerEntry> builder)
        {
            builder.ToTable("LedgerEntries");

            builder.HasKey(l => l.Id);

            builder.Property(l => l.GamerAccountId)
                .IsRequired();

            builder.Property(l => l.Amount)
                .HasPrecision(18, 4)
                .IsRequired();

            builder.Property(l => l.Currency)
                .IsRequired()
                .HasMaxLength(10)
                .HasDefaultValue("SAY");

            builder.Property(l => l.Direction)
                .IsRequired()
                .HasMaxLength(10);

            builder.Property(l => l.EntryType)
                .IsRequired()
                .HasMaxLength(50);

            builder.Property(l => l.Reference)
                .IsRequired()
                .HasMaxLength(100);

            builder.Property(l => l.CorrelationId)
                .HasMaxLength(100);

            builder.Property(l => l.Actor)
                .HasMaxLength(100);

            builder.Property(l => l.Description)
                .HasMaxLength(500);

            builder.Property(l => l.BalanceAfter)
                .HasPrecision(18, 4)
                .IsRequired();

            builder.Property(l => l.CreatedAtUtc)
                .IsRequired();

            builder.HasIndex(l => l.GamerAccountId)
                .HasDatabaseName("IX_LedgerEntries_GamerAccountId");

            builder.HasIndex(l => new { l.GamerAccountId, l.CreatedAtUtc })
                .HasDatabaseName("IX_LedgerEntries_GamerAccountId_CreatedAtUtc");

            builder.HasIndex(l => l.Reference)
                .HasDatabaseName("IX_LedgerEntries_Reference");

            builder.HasIndex(l => l.CorrelationId)
                .HasDatabaseName("IX_LedgerEntries_CorrelationId");

            builder.HasOne<GamerAccount>()
                .WithMany()
                .HasForeignKey(l => l.GamerAccountId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
