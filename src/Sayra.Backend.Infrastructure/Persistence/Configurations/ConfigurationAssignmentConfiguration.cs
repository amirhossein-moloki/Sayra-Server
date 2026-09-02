using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain.Entities;

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

            builder.Property(a => a.Priority)
                .IsRequired()
                .HasDefaultValue(0);

            builder.Property(a => a.AssignedBy)
                .IsRequired()
                .HasMaxLength(256);

            builder.Property(a => a.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();

            builder.HasOne(a => a.Package)
                .WithMany()
                .HasForeignKey(a => a.ConfigurationPackageId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(a => a.Target)
                .WithMany()
                .HasForeignKey(a => a.ConfigurationTargetId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex(a => new { a.ConfigurationPackageId, a.ConfigurationTargetId, a.IsActive })
                .HasDatabaseName("IX_configuration_assignments_Package_Target_IsActive");

            builder.HasIndex(a => new { a.ConfigurationTargetId, a.IsActive, a.Priority })
                .HasDatabaseName("IX_configuration_assignments_Target_IsActive_Priority");

            builder.Ignore(a => a.DomainEvents);
        }
    }
}
