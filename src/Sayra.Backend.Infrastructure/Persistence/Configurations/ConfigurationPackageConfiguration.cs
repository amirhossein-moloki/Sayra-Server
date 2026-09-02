using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class ConfigurationPackageConfiguration : IEntityTypeConfiguration<ConfigurationPackage>
    {
        public void Configure(EntityTypeBuilder<ConfigurationPackage> builder)
        {
            builder.ToTable("configuration_packages");

            builder.HasKey(c => c.Id);

            builder.Property(c => c.PackageId)
                .IsRequired()
                .HasMaxLength(128);

            builder.Property(c => c.Name)
                .IsRequired()
                .HasMaxLength(128);

            builder.Property(c => c.Version)
                .HasConversion(
                    v => v.ToString(),
                    v => ConfigurationVersion.Parse(v))
                .IsRequired()
                .HasMaxLength(64);

            builder.Property(c => c.BaseVersion)
                .HasConversion(
                    v => v != null ? v.ToString() : null,
                    v => v != null ? ConfigurationVersion.Parse(v) : null)
                .HasMaxLength(64);

            builder.Property(c => c.PayloadType)
                .HasConversion(
                    v => v.ToString(),
                    v => ConfigurationPayloadTypeExtensions.ParsePayloadType(v))
                .IsRequired()
                .HasMaxLength(32);

            builder.Property(c => c.Status)
                .HasConversion(
                    v => v.ToString(),
                    v => ConfigurationStatusExtensions.ParseStatus(v))
                .IsRequired()
                .HasMaxLength(32);

            builder.Property(c => c.Content)
                .HasColumnType("jsonb")
                .IsRequired()
                .HasDefaultValue("{}");

            builder.Property(c => c.ContentHash)
                .HasMaxLength(128);

            builder.Property(c => c.Signature)
                .HasMaxLength(1024);

            builder.Property(c => c.SignerIdentity)
                .HasMaxLength(256);

            builder.Property(c => c.CreatedBy)
                .IsRequired()
                .HasMaxLength(256);

            builder.Property(c => c.PublishedBy)
                .HasMaxLength(256);

            builder.Property(c => c.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();

            builder.HasIndex(c => new { c.PackageId, c.Version })
                .HasDatabaseName("IX_configuration_packages_PackageId_Version")
                .IsUnique();

            builder.HasIndex(c => new { c.Name, c.Version })
                .HasDatabaseName("IX_configuration_packages_Name_Version")
                .IsUnique();

            builder.HasIndex(c => c.Status)
                .HasDatabaseName("IX_configuration_packages_Status");

            builder.HasIndex(c => c.PayloadType)
                .HasDatabaseName("IX_configuration_packages_PayloadType");

            builder.HasIndex(c => c.CreatedAt)
                .HasDatabaseName("IX_configuration_packages_CreatedAt");

            builder.Ignore(c => c.DomainEvents);
        }
    }
}
