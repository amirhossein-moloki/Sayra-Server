// PHASE 03 — STAGE 03-09: Payment / Financial Transaction Engine & Idempotency
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class FinancialTransactionConfiguration : IEntityTypeConfiguration<FinancialTransaction>
    {
        public void Configure(EntityTypeBuilder<FinancialTransaction> builder)
        {
            builder.ToTable("FinancialTransactions");

            builder.HasKey(f => f.Id);

            builder.Property(f => f.GamerAccountId)
                .IsRequired();

            builder.Property(f => f.OperationType)
                .IsRequired()
                .HasMaxLength(50);

            builder.Property(f => f.Amount)
                .HasPrecision(18, 4)
                .IsRequired();

            builder.Property(f => f.Currency)
                .IsRequired()
                .HasMaxLength(10)
                .HasDefaultValue("SAY");

            builder.Property(f => f.Status)
                .IsRequired()
                .HasMaxLength(20);

            builder.Property(f => f.IdempotencyKey)
                .IsRequired()
                .HasMaxLength(100);

            builder.Property(f => f.RequestFingerprint)
                .IsRequired()
                .HasMaxLength(128);

            builder.Property(f => f.CorrelationId)
                .HasMaxLength(100);

            builder.Property(f => f.ReferenceId)
                .HasMaxLength(100);

            builder.Property(f => f.FailureReason)
                .HasMaxLength(500);

            builder.Property(f => f.CreatedAtUtc)
                .IsRequired();

            // Idempotency uniqueness invariant enforced at DB level
            builder.HasIndex(f => f.IdempotencyKey)
                .IsUnique()
                .HasDatabaseName("IX_FinancialTransactions_IdempotencyKey");

            builder.HasIndex(f => f.GamerAccountId)
                .HasDatabaseName("IX_FinancialTransactions_GamerAccountId");

            builder.HasIndex(f => f.Status)
                .HasDatabaseName("IX_FinancialTransactions_Status");

            builder.HasIndex(f => f.CorrelationId)
                .HasDatabaseName("IX_FinancialTransactions_CorrelationId");

            builder.HasIndex(f => f.OriginalTransactionId)
                .HasDatabaseName("IX_FinancialTransactions_OriginalTransactionId");

            builder.HasOne<GamerAccount>()
                .WithMany()
                .HasForeignKey(f => f.GamerAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne<LedgerEntry>()
                .WithMany()
                .HasForeignKey(f => f.LedgerEntryId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne<FinancialTransaction>()
                .WithMany()
                .HasForeignKey(f => f.OriginalTransactionId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
