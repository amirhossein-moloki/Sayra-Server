using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class PricingRuleConfiguration : IEntityTypeConfiguration<PricingRule>
    {
        public void Configure(EntityTypeBuilder<PricingRule> builder)
        {
            builder.ToTable("pricing_rules");

            builder.HasKey(r => r.Id);

            builder.Ignore(r => r.PricingRuleId);

            builder.Property(r => r.PricingPlanId)
                .IsRequired();

            builder.Property(r => r.Name)
                .IsRequired()
                .HasMaxLength(100);

            builder.Property(r => r.RateAmount)
                .HasColumnType("numeric(18, 4)")
                .IsRequired();

            builder.Property(r => r.Currency)
                .IsRequired()
                .HasMaxLength(10);

            builder.Property(r => r.Priority)
                .IsRequired();

            builder.Property(r => r.WorkstationId)
                .IsRequired(false);

            builder.Property(r => r.ZoneId)
                .IsRequired(false);

            builder.Property(r => r.GamerType)
                .HasMaxLength(50)
                .IsRequired(false);

            builder.Property(r => r.DayOfWeek)
                .IsRequired(false);

            builder.Property(r => r.StartTime)
                .IsRequired(false);

            builder.Property(r => r.EndTime)
                .IsRequired(false);

            builder.Property(r => r.IsPeak)
                .IsRequired(false);

            builder.HasIndex(r => new { r.PricingPlanId, r.Priority })
                .IsUnique()
                .HasDatabaseName("IX_pricing_rules_PricingPlanId_Priority");

            builder.HasOne<PricingPlan>()
                .WithMany()
                .HasForeignKey(r => r.PricingPlanId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne<Workstation>()
                .WithMany()
                .HasForeignKey(r => r.WorkstationId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);

            builder.HasOne<Zone>()
                .WithMany()
                .HasForeignKey(r => r.ZoneId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
        }
    }
}
