using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
    {
        public void Configure(EntityTypeBuilder<Organization> builder)
        {
            builder.ToTable("Organizations");

            builder.HasKey(o => o.Id);

            builder.Property(o => o.OrganizationId)
                .IsRequired()
                .HasMaxLength(100);

            builder.Property(o => o.Name)
                .IsRequired()
                .HasMaxLength(100);

            builder.Property(o => o.Code)
                .IsRequired()
                .HasMaxLength(50);

            builder.Property(o => o.Status)
                .IsRequired()
                .HasMaxLength(50)
                .HasDefaultValue("Active");

            builder.Property(o => o.CreatedAt)
                .IsRequired();

            // Unique constraint on Organization Code
            builder.HasIndex(o => o.Code).IsUnique();
        }
    }
}
