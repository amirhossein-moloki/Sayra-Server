using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class UpdatePackageConfiguration : IEntityTypeConfiguration<UpdatePackage>
    {
        public void Configure(EntityTypeBuilder<UpdatePackage> builder)
        {
            builder.ToTable("update_packages");

            builder.HasKey(p => p.Id);

            builder.Property(p => p.ReleaseId)
                .IsRequired();

            builder.Property(p => p.FileName)
                .IsRequired()
                .HasMaxLength(256);

            builder.Property(p => p.Size)
                .IsRequired();

            builder.Property(p => p.SHA256)
                .HasMaxLength(64)
                .IsRequired(false);

            builder.Property(p => p.Signature)
                .HasMaxLength(2048)
                .IsRequired(false);

            builder.Property(p => p.SigningKeyId)
                .HasMaxLength(100)
                .IsRequired(false);

            builder.Property(p => p.StorageProvider)
                .IsRequired()
                .HasMaxLength(50)
                .HasDefaultValue("local");

            builder.Property(p => p.StorageKey)
                .IsRequired()
                .HasMaxLength(256);

            builder.Property(p => p.PackageType)
                .HasConversion<string>()
                .IsRequired()
                .HasMaxLength(30);

            builder.Property(p => p.LifecycleState)
                .HasConversion<string>()
                .IsRequired()
                .HasMaxLength(30);

            builder.Property(p => p.VerificationStatus)
                .HasConversion<string>()
                .IsRequired()
                .HasMaxLength(30);

            builder.Property(p => p.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();

            // Navigation relationship defined in UpdateReleaseConfiguration as well
            builder.HasOne(p => p.Release)
                .WithMany(r => r.Packages)
                .HasForeignKey(p => p.ReleaseId)
                .OnDelete(DeleteBehavior.Cascade);

            // Indexes
            builder.HasIndex(p => p.StorageKey).IsUnique();
            builder.HasIndex(p => p.ReleaseId);
            builder.HasIndex(p => p.SHA256);
            builder.HasIndex(p => p.LifecycleState);
        }
    }
}
