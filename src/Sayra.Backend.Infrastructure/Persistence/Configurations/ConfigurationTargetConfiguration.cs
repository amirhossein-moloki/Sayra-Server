using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class ConfigurationTargetConfiguration : IEntityTypeConfiguration<ConfigurationTarget>
    {
        public void Configure(EntityTypeBuilder<ConfigurationTarget> builder)
        {
            builder.ToTable("configuration_targets");

            builder.HasKey(t => t.Id);

            builder.Property(t => t.TargetType)
                .HasConversion<string>()
                .IsRequired()
                .HasMaxLength(20);

            builder.Property(t => t.OrganizationId)
                .IsRequired();

            builder.Property(t => t.SiteId)
                .IsRequired(false);

            builder.Property(t => t.GroupId)
                .IsRequired(false);

            builder.Property(t => t.WorkstationId)
                .IsRequired(false);

            builder.HasIndex(t => new { t.OrganizationId, t.TargetType });
            builder.HasIndex(t => t.SiteId);
            builder.HasIndex(t => t.GroupId);
            builder.HasIndex(t => t.WorkstationId);

            builder.HasIndex(t => new { t.OrganizationId, t.TargetType, t.SiteId, t.GroupId, t.WorkstationId }).IsUnique();
        }
    }
}
