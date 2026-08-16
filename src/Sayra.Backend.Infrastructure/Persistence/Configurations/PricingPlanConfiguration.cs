using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class PricingPlanConfiguration : IEntityTypeConfiguration<PricingPlan>
    {
        public void Configure(EntityTypeBuilder<PricingPlan> builder)
        {
            builder.ToTable("pricing_plans");

            builder.HasKey(p => p.Id);

            builder.Ignore(p => p.PricingPlanId);
            builder.Ignore(p => p.IsActive);

            builder.Property(p => p.SiteId)
                .IsRequired();

            builder.Property(p => p.Name)
                .IsRequired()
                .HasMaxLength(100);

            builder.Property(p => p.Status)
                .IsRequired()
                .HasMaxLength(20);

            builder.Property(p => p.Currency)
                .IsRequired()
                .HasMaxLength(10);

            builder.HasIndex(p => new { p.SiteId, p.Name })
                .IsUnique()
                .HasDatabaseName("IX_pricing_plans_SiteId_Name");

            builder.HasOne<Site>()
                .WithMany()
                .HasForeignKey(p => p.SiteId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
