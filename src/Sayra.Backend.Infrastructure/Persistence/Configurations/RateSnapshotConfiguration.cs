using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class RateSnapshotConfiguration : IEntityTypeConfiguration<RateSnapshot>
    {
        public void Configure(EntityTypeBuilder<RateSnapshot> builder)
        {
            builder.ToTable("rate_snapshots");

            builder.HasKey(s => s.Id);

            builder.Ignore(s => s.RateSnapshotId);

            builder.Property(s => s.SessionId)
                .IsRequired();

            builder.Property(s => s.PricingPlanId)
                .IsRequired();

            builder.Property(s => s.PricingRuleId)
                .IsRequired(false);

            builder.Property(s => s.RateAmount)
                .HasColumnType("numeric(18, 4)")
                .IsRequired();

            builder.Property(s => s.Currency)
                .IsRequired()
                .HasMaxLength(10);

            builder.Property(s => s.AppliedAtUtc)
                .IsRequired();

            builder.Property(s => s.RuleReference)
                .IsRequired()
                .HasMaxLength(200);

            builder.HasIndex(s => s.SessionId)
                .IsUnique()
                .HasDatabaseName("IX_rate_snapshots_SessionId");

            builder.HasOne<Session>()
                .WithMany()
                .HasForeignKey(s => s.SessionId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne<PricingPlan>()
                .WithMany()
                .HasForeignKey(s => s.PricingPlanId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne<PricingRule>()
                .WithMany()
                .HasForeignKey(s => s.PricingRuleId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
        }
    }
}
