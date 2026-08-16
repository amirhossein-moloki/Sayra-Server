using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class BillingResultConfiguration : IEntityTypeConfiguration<BillingResult>
    {
        public void Configure(EntityTypeBuilder<BillingResult> builder)
        {
            builder.ToTable("billing_results");

            builder.HasKey(b => b.Id);

            builder.Ignore(b => b.BillingResultId);

            builder.Property(b => b.SessionId)
                .IsRequired();

            builder.Property(b => b.ConsumedDuration)
                .IsRequired();

            builder.Property(b => b.RateSnapshotId)
                .IsRequired();

            builder.OwnsOne(b => b.Subtotal, subtotalBuilder =>
            {
                subtotalBuilder.Property(m => m.Amount)
                    .HasColumnName("Subtotal")
                    .HasColumnType("numeric(18, 4)")
                    .IsRequired();

                subtotalBuilder.Property(m => m.Currency)
                    .HasColumnName("SubtotalCurrency")
                    .HasMaxLength(10)
                    .IsRequired();
            });

            builder.OwnsOne(b => b.DiscountAmount, discountBuilder =>
            {
                discountBuilder.Property(m => m.Amount)
                    .HasColumnName("DiscountAmount")
                    .HasColumnType("numeric(18, 4)")
                    .IsRequired();

                discountBuilder.Property(m => m.Currency)
                    .HasColumnName("DiscountCurrency")
                    .HasMaxLength(10)
                    .IsRequired();
            });

            builder.OwnsOne(b => b.AdjustmentAmount, adjustmentBuilder =>
            {
                adjustmentBuilder.Property(m => m.Amount)
                    .HasColumnName("AdjustmentAmount")
                    .HasColumnType("numeric(18, 4)")
                    .IsRequired();

                adjustmentBuilder.Property(m => m.Currency)
                    .HasColumnName("AdjustmentCurrency")
                    .HasMaxLength(10)
                    .IsRequired();
            });

            builder.OwnsOne(b => b.FinalAmount, finalBuilder =>
            {
                finalBuilder.Property(m => m.Amount)
                    .HasColumnName("FinalAmount")
                    .HasColumnType("numeric(18, 4)")
                    .IsRequired();

                finalBuilder.Property(m => m.Currency)
                    .HasColumnName("FinalCurrency")
                    .HasMaxLength(10)
                    .IsRequired();
            });

            builder.Property(b => b.Currency)
                .IsRequired()
                .HasMaxLength(10);

            builder.Property(b => b.CalculatedAtUtc)
                .IsRequired();

            builder.Property(b => b.CorrelationId)
                .HasMaxLength(100)
                .IsRequired(false);

            builder.HasIndex(b => b.SessionId)
                .HasDatabaseName("IX_billing_results_SessionId");

            builder.HasIndex(b => b.CalculatedAtUtc)
                .HasDatabaseName("IX_billing_results_CalculatedAtUtc");

            builder.HasIndex(b => b.RateSnapshotId)
                .HasDatabaseName("IX_billing_results_RateSnapshotId");

            builder.HasOne<Session>()
                .WithMany()
                .HasForeignKey(b => b.SessionId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne<RateSnapshot>()
                .WithMany()
                .HasForeignKey(b => b.RateSnapshotId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
