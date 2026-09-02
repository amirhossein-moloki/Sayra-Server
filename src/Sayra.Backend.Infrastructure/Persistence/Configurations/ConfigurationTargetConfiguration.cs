using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class ConfigurationTargetConfiguration : IEntityTypeConfiguration<ConfigurationTarget>
    {
        public void Configure(EntityTypeBuilder<ConfigurationTarget> builder)
        {
            builder.ToTable("configuration_targets");

            builder.HasKey(t => t.Id);

            builder.Property(t => t.TargetType)
                .HasConversion(
                    v => v.ToString(),
                    v => ConfigurationTargetTypeExtensions.ParseTargetType(v))
                .IsRequired()
                .HasMaxLength(32);

            builder.Property(t => t.TargetIdentifier)
                .IsRequired()
                .HasMaxLength(128);

            builder.Property(t => t.Description)
                .HasMaxLength(512);

            builder.HasOne<Site>()
                .WithMany()
                .HasForeignKey(t => t.SiteEntityId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);

            builder.HasOne<Workstation>()
                .WithMany()
                .HasForeignKey(t => t.WorkstationEntityId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);

            builder.HasIndex(t => new { t.TargetType, t.TargetIdentifier })
                .HasDatabaseName("IX_configuration_targets_TargetType_TargetIdentifier")
                .IsUnique();

            builder.HasIndex(t => t.SiteEntityId)
                .HasDatabaseName("IX_configuration_targets_SiteEntityId");

            builder.HasIndex(t => t.WorkstationEntityId)
                .HasDatabaseName("IX_configuration_targets_WorkstationEntityId");

            builder.Ignore(t => t.DomainEvents);
        }
    }
}
