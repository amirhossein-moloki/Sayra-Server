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

            builder.Property(c => c.VersionNumber)
                .IsRequired();

            builder.Property(c => c.Version)
                .IsRequired()
                .HasMaxLength(50);

            builder.Property(c => c.BaseVersionNumber)
                .IsRequired(false);

            builder.Property(c => c.PayloadType)
                .HasConversion<string>()
                .IsRequired()
                .HasMaxLength(20);

            builder.Property(c => c.SchemaVersion)
                .IsRequired()
                .HasMaxLength(20)
                .HasDefaultValue("1.0");

            builder.Property(c => c.Content)
                .HasColumnType("jsonb")
                .IsRequired()
                .HasDefaultValue("{}");

            builder.Property(c => c.IssuedBy)
                .IsRequired()
                .HasMaxLength(100)
                .HasDefaultValue("system");

            builder.Property(c => c.IsActive)
                .IsRequired()
                .HasDefaultValue(true);

            builder.Property(c => c.ConfigurationHash)
                .HasMaxLength(64)
                .IsRequired(false);

            builder.Property(c => c.Signature)
                .HasMaxLength(1024)
                .IsRequired(false);

            builder.Property(c => c.SignatureAlgorithm)
                .HasMaxLength(50)
                .IsRequired(false);

            builder.Property(c => c.SigningKeyId)
                .HasMaxLength(100)
                .IsRequired(false);

            builder.Property(c => c.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();

            builder.HasIndex(c => new { c.Name, c.VersionNumber }).IsUnique();
        }
    }
}
