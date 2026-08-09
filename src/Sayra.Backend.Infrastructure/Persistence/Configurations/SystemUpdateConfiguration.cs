using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class SystemUpdateConfiguration : IEntityTypeConfiguration<SystemUpdate>
    {
        public void Configure(EntityTypeBuilder<SystemUpdate> builder)
        {
            builder.ToTable("SystemUpdates");

            builder.HasKey(u => u.Id);

            builder.Property(u => u.Version)
                .IsRequired()
                .HasMaxLength(50);

            builder.Property(u => u.ChecksumSha256)
                .IsRequired()
                .HasMaxLength(64);

            builder.Property(u => u.FilePath)
                .IsRequired()
                .HasMaxLength(500);

            builder.Property(u => u.DigitalSignature)
                .HasColumnType("bytea");

            builder.Property(u => u.ReleaseDate)
                .IsRequired();

            builder.Property(u => u.IsMandatory)
                .IsRequired()
                .HasDefaultValue(false);

            builder.HasIndex(u => u.Version).IsUnique();
        }
    }
}
