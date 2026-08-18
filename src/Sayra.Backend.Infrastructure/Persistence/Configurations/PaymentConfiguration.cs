// PHASE 03 — STAGE 03-09: Payment / Financial Transaction Engine & Idempotency
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
    {
        public void Configure(EntityTypeBuilder<Payment> builder)
        {
            builder.ToTable("Payments");

            builder.HasKey(p => p.Id);

            builder.Property(p => p.GamerAccountId)
                .IsRequired();

            builder.Property(p => p.Amount)
                .HasPrecision(18, 4)
                .IsRequired();

            builder.Property(p => p.Currency)
                .IsRequired()
                .HasMaxLength(10)
                .HasDefaultValue("SAY");

            builder.Property(p => p.Status)
                .IsRequired()
                .HasMaxLength(20);

            builder.Property(p => p.PaymentMethod)
                .IsRequired()
                .HasMaxLength(50);

            builder.Property(p => p.IdempotencyKey)
                .IsRequired()
                .HasMaxLength(100);

            builder.Property(p => p.Reference)
                .HasMaxLength(100);

            builder.Property(p => p.Description)
                .HasMaxLength(500);

            builder.Property(p => p.FailureReason)
                .HasMaxLength(500);

            builder.Property(p => p.CreatedAtUtc)
                .IsRequired();

            builder.HasIndex(p => p.IdempotencyKey)
                .IsUnique()
                .HasDatabaseName("IX_Payments_IdempotencyKey");

            builder.HasIndex(p => p.GamerAccountId)
                .HasDatabaseName("IX_Payments_GamerAccountId");

            builder.HasIndex(p => p.FinancialTransactionId)
                .HasDatabaseName("IX_Payments_FinancialTransactionId");

            builder.HasOne<GamerAccount>()
                .WithMany()
                .HasForeignKey(p => p.GamerAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne<FinancialTransaction>()
                .WithMany()
                .HasForeignKey(p => p.FinancialTransactionId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
