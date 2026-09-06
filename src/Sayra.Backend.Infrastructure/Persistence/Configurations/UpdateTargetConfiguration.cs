using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class UpdateTargetConfiguration : IEntityTypeConfiguration<UpdateTarget>
    {
        public void Configure(EntityTypeBuilder<UpdateTarget> builder)
        {
            builder.ToTable("update_targets");

            builder.HasKey(t => t.Id);

            builder.Property(t => t.OrganizationId)
                .IsRequired();

            builder.Property(t => t.ReleaseId)
                .IsRequired();

            builder.Property(t => t.TargetType)
                .HasConversion<string>()
                .IsRequired()
                .HasMaxLength(20);

            builder.Property(t => t.SiteId)
                .IsRequired(false);

            builder.Property(t => t.GroupId)
                .IsRequired(false);

            builder.Property(t => t.WorkstationId)
                .IsRequired(false);

            builder.Property(t => t.RolloutPercentage)
                .IsRequired()
                .HasDefaultValue(100);

            builder.Property(t => t.IsEnabled)
                .IsRequired()
                .HasDefaultValue(true);

            builder.Property(t => t.MinimumSupportedVersion)
                .HasMaxLength(64)
                .IsRequired(false);

            builder.Property(t => t.IsMandatoryOverride)
                .IsRequired()
                .HasDefaultValue(false);

            builder.Property(t => t.CreatedBy)
                .IsRequired()
                .HasMaxLength(100)
                .HasDefaultValue("system");

            builder.Property(t => t.RowVersion)
                .IsRowVersion();

            // Foreign keys
            builder.HasOne(t => t.Release)
                .WithMany()
                .HasForeignKey(t => t.ReleaseId)
                .OnDelete(DeleteBehavior.Cascade);

            // Indexes for fast targeting lookups
            builder.HasIndex(t => new { t.OrganizationId, t.TargetType, t.IsEnabled });
            builder.HasIndex(t => t.ReleaseId);
            builder.HasIndex(t => t.SiteId);
            builder.HasIndex(t => t.GroupId);
            builder.HasIndex(t => t.WorkstationId);

            // Unique constraint preventing duplicate target scopes for the same release
            builder.HasIndex(t => new { t.OrganizationId, t.ReleaseId, t.TargetType, t.SiteId, t.GroupId, t.WorkstationId })
                .IsUnique();
        }
    }
}
