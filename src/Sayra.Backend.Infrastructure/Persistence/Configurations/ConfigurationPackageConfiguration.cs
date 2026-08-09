using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class ConfigurationPackageConfiguration : IEntityTypeConfiguration<ConfigurationPackage>
    {
        public void Configure(EntityTypeBuilder<ConfigurationPackage> builder)
        {
            builder.ToTable("ConfigurationPackages");

            builder.HasKey(c => c.Id);

            builder.Property(c => c.Name)
                .IsRequired()
                .HasMaxLength(100);

            builder.Property(c => c.Version)
                .IsRequired()
                .HasMaxLength(50);

            builder.Property(c => c.Content)
                .HasColumnType("jsonb")
                .IsRequired()
                .HasDefaultValue("{}");

            builder.Property(c => c.IsActive)
                .IsRequired()
                .HasDefaultValue(false);

            builder.Property(c => c.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();

            builder.HasIndex(c => new { c.Name, c.Version }).IsUnique();
        }
    }
}
