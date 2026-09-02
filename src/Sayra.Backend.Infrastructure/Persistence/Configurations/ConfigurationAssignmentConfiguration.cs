using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class ConfigurationAssignmentConfiguration : IEntityTypeConfiguration<ConfigurationAssignment>
    {
        public void Configure(EntityTypeBuilder<ConfigurationAssignment> builder)
        {
            builder.ToTable("configuration_assignments");

            builder.HasKey(a => a.Id);

            builder.Property(a => a.ConfigurationPackageId)
                .IsRequired();

            builder.Property(a => a.ConfigurationTargetId)
                .IsRequired();

            builder.Property(a => a.IsActive)
                .IsRequired()
                .HasDefaultValue(true);

            builder.Property(a => a.AssignedBy)
                .IsRequired()
                .HasMaxLength(100)
                .HasDefaultValue("system");

            builder.Property(a => a.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();

            builder.HasIndex(a => new { a.ConfigurationPackageId, a.ConfigurationTargetId }).IsUnique();
            builder.HasIndex(a => new { a.ConfigurationTargetId, a.IsActive });
        }
    }
}
