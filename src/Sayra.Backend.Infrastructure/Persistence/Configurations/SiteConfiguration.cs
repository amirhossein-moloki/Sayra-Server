using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class SiteConfiguration : IEntityTypeConfiguration<Site>
    {
        public void Configure(EntityTypeBuilder<Site> builder)
        {
            builder.ToTable("Sites");

            builder.HasKey(s => s.Id);

            builder.Property(s => s.SiteId)
                .IsRequired()
                .HasMaxLength(100);

            builder.Property(s => s.OrganizationId)
                .IsRequired();

            builder.Property(s => s.Name)
                .IsRequired()
                .HasMaxLength(100);

            builder.Property(s => s.Code)
                .IsRequired()
                .HasMaxLength(50);

            builder.Property(s => s.Status)
                .IsRequired()
                .HasMaxLength(50)
                .HasDefaultValue("Active");

            builder.Property(s => s.Timezone)
                .IsRequired()
                .HasMaxLength(100)
                .HasDefaultValue("UTC");

            builder.Property(s => s.CreatedAt)
                .IsRequired();

            // Foreign key to Organization with Restrict delete behavior
            builder.HasOne<Organization>()
                .WithMany()
                .HasForeignKey(s => s.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);

            // Unique constraint on (OrganizationId, Code)
            builder.HasIndex(s => new { s.OrganizationId, s.Code }).IsUnique();
        }
    }
}
