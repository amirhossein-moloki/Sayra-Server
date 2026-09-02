using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;

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

            builder.Property(p => p.Status)
                .HasConversion(
                    v => v.ToString(),
                    v => ConfigurationStatusExtensions.ParseStatus(v))
                .IsRequired()
                .HasMaxLength(32);

            builder.Property(p => p.PublishedAt)
                .IsRequired();

            builder.Property(p => p.PublishedBy)
                .IsRequired()
                .HasMaxLength(256);

            builder.Property(p => p.Notes)
                .HasMaxLength(1024);

            builder.Property(p => p.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();

            builder.HasOne(p => p.Package)
                .WithMany()
                .HasForeignKey(p => p.ConfigurationPackageId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(p => p.Target)
                .WithMany()
                .HasForeignKey(p => p.ConfigurationTargetId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);

            builder.HasIndex(p => new { p.ConfigurationPackageId, p.Status })
                .HasDatabaseName("IX_configuration_publications_Package_Status");

            builder.HasIndex(p => new { p.ConfigurationTargetId, p.Status })
                .HasDatabaseName("IX_configuration_publications_Target_Status");

            builder.Ignore(p => p.DomainEvents);
        }
    }
}
