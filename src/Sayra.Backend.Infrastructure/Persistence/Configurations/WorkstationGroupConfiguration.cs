using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class WorkstationGroupConfiguration : IEntityTypeConfiguration<WorkstationGroup>
    {
        public void Configure(EntityTypeBuilder<WorkstationGroup> builder)
        {
            builder.ToTable("workstation_groups");

            builder.HasKey(g => g.Id);

            builder.Property(g => g.OrganizationId)
                .IsRequired();

            builder.Property(g => g.SiteId)
                .IsRequired(false);

            builder.Property(g => g.Name)
                .IsRequired()
                .HasMaxLength(100);

            builder.Property(g => g.Code)
                .IsRequired()
                .HasMaxLength(50);

            builder.Property(g => g.Status)
                .IsRequired()
                .HasMaxLength(20)
                .HasDefaultValue("Active");

            builder.Property(g => g.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();

            builder.HasIndex(g => new { g.OrganizationId, g.Code }).IsUnique();
            builder.HasIndex(g => g.SiteId);
        }
    }
}
