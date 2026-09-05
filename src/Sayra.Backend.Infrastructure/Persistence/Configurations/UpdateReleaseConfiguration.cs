using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class UpdateReleaseConfiguration : IEntityTypeConfiguration<UpdateRelease>
    {
        public void Configure(EntityTypeBuilder<UpdateRelease> builder)
        {
            builder.ToTable("update_releases");

            builder.HasKey(r => r.Id);

            builder.Property(r => r.OrganizationId)
                .IsRequired();

            builder.Property(r => r.Version)
                .IsRequired()
                .HasMaxLength(64);

            builder.Property(r => r.ReleaseType)
                .HasConversion<string>()
                .IsRequired()
                .HasMaxLength(30);

            builder.Property(r => r.Status)
                .HasConversion<string>()
                .IsRequired()
                .HasMaxLength(30);

            builder.Property(r => r.ReleaseNotes)
                .HasMaxLength(2048)
                .IsRequired(false);

            builder.Property(r => r.CreatedBy)
                .IsRequired()
                .HasMaxLength(100)
                .HasDefaultValue("system");

            builder.Property(r => r.PublishedAt)
                .IsRequired(false);

            builder.Property(r => r.RevokedAt)
                .IsRequired(false);

            builder.Property(r => r.SupersededAt)
                .IsRequired(false);

            builder.Property(r => r.Metadata)
                .HasColumnType("text")
                .IsRequired(false);

            builder.Property(r => r.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();

            // Foreign Key to Organization
            builder.HasOne<Organization>()
                .WithMany()
                .HasForeignKey(r => r.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);

            // One-to-Many Packages relationship
            builder.HasMany(r => r.Packages)
                .WithOne(p => p.Release)
                .HasForeignKey(p => p.ReleaseId)
                .OnDelete(DeleteBehavior.Cascade);

            // Indexes
            builder.HasIndex(r => new { r.OrganizationId, r.Version }).IsUnique();
            builder.HasIndex(r => new { r.OrganizationId, r.Status });
            builder.HasIndex(r => r.CreatedAt);
        }
    }
}
