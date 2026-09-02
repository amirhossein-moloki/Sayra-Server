using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class ConfigurationPublicationConfiguration : IEntityTypeConfiguration<ConfigurationPublication>
    {
        public void Configure(EntityTypeBuilder<ConfigurationPublication> builder)
        {
            builder.ToTable("configuration_publications");

            builder.HasKey(p => p.Id);

            builder.Property(p => p.ConfigurationPackageId)
                .IsRequired();

            builder.Property(p => p.VersionNumber)
                .IsRequired();

            builder.Property(p => p.Version)
                .IsRequired()
                .HasMaxLength(50);

            builder.Property(p => p.ConfigurationTargetId)
                .IsRequired();

            builder.Property(p => p.OrganizationId)
                .IsRequired();

            builder.Property(p => p.Status)
                .HasConversion<string>()
                .IsRequired()
                .HasMaxLength(30);

            builder.Property(p => p.IssuedBy)
                .IsRequired()
                .HasMaxLength(100)
                .HasDefaultValue("system");

            builder.Property(p => p.PublishedAt)
                .IsRequired(false);

            builder.Property(p => p.ActivatedAt)
                .IsRequired(false);

            builder.Property(p => p.SupersededAt)
                .IsRequired(false);

            builder.Property(p => p.SupersededByPublicationId)
                .IsRequired(false);

            builder.Property(p => p.RevokedAt)
                .IsRequired(false);

            builder.Property(p => p.RevokedBy)
                .HasMaxLength(100)
                .IsRequired(false);

            builder.Property(p => p.RevocationReason)
                .HasMaxLength(500)
                .IsRequired(false);

            builder.Property(p => p.CorrelationId)
                .HasMaxLength(100)
                .IsRequired(false);

            builder.Property(p => p.Notes)
                .HasMaxLength(500)
                .IsRequired(false);

            builder.Property(p => p.IdempotencyKey)
                .HasMaxLength(100)
                .IsRequired(false);

            builder.Property(p => p.IsRollback)
                .IsRequired()
                .HasDefaultValue(false);

            builder.Property(p => p.SourceVersionNumber)
                .IsRequired(false);

            builder.Property(p => p.FailedVersionNumber)
                .IsRequired(false);

            builder.Property(p => p.SourcePublicationId)
                .IsRequired(false);

            builder.Property(p => p.ConfigurationHash)
                .IsRequired()
                .HasMaxLength(64);

            builder.Property(p => p.Signature)
                .IsRequired()
                .HasMaxLength(1024);

            builder.Property(p => p.SignatureAlgorithm)
                .IsRequired()
                .HasMaxLength(50)
                .HasDefaultValue("RSA-SHA256");

            builder.Property(p => p.SigningKeyId)
                .IsRequired()
                .HasMaxLength(100);

            builder.Property(p => p.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();

            builder.HasIndex(p => p.IdempotencyKey)
                .IsUnique()
                .HasFilter("\"IdempotencyKey\" IS NOT NULL");

            builder.HasIndex(p => p.ConfigurationTargetId)
                .IsUnique()
                .HasFilter("\"Status\" = 'Active'");

            builder.HasIndex(p => new { p.ConfigurationTargetId, p.Status });
            builder.HasIndex(p => new { p.ConfigurationPackageId, p.ConfigurationTargetId });
            builder.HasIndex(p => new { p.OrganizationId, p.ConfigurationTargetId });
        }
    }
}
