using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class ZoneConfiguration : IEntityTypeConfiguration<Zone>
    {
        public void Configure(EntityTypeBuilder<Zone> builder)
        {
            builder.ToTable("Zones");

            builder.HasKey(z => z.Id);

            builder.Property(z => z.ZoneId)
                .IsRequired()
                .HasMaxLength(100);

            builder.Property(z => z.SiteId)
                .IsRequired();

            builder.Property(z => z.Name)
                .IsRequired()
                .HasMaxLength(100);

            builder.Property(z => z.Code)
                .IsRequired()
                .HasMaxLength(50);

            builder.Property(z => z.Status)
                .IsRequired()
                .HasMaxLength(50)
                .HasDefaultValue("Active");

            builder.Property(z => z.CreatedAt)
                .IsRequired();

            // Foreign key to Site with Restrict delete behavior
            builder.HasOne<Site>()
                .WithMany()
                .HasForeignKey(z => z.SiteId)
                .OnDelete(DeleteBehavior.Restrict);

            // Unique constraint on (SiteId, Code)
            builder.HasIndex(z => new { z.SiteId, z.Code }).IsUnique();
        }
    }
}
