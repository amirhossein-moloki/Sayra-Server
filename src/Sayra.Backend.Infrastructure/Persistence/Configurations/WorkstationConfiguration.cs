using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class WorkstationConfiguration : IEntityTypeConfiguration<Workstation>
    {
        public void Configure(EntityTypeBuilder<Workstation> builder)
        {
            builder.ToTable("Workstations");

            builder.HasKey(w => w.Id);

            builder.Property(w => w.Name)
                .IsRequired()
                .HasMaxLength(100);

            builder.Property(w => w.IpAddress)
                .IsRequired()
                .HasMaxLength(50);

            builder.Property(w => w.MacAddress)
                .IsRequired()
                .HasMaxLength(50);

            builder.Property(w => w.Status)
                .IsRequired()
                .HasMaxLength(50)
                .HasDefaultValue("Offline");

            builder.Property(w => w.LastSeen)
                .IsRequired();

            builder.Property(w => w.VerificationPublicKey)
                .HasColumnType("bytea");

            builder.Property(w => w.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();

            // Indexing for workstation queries
            builder.HasIndex(w => w.IpAddress).IsUnique();
            builder.HasIndex(w => w.MacAddress).IsUnique();
        }
    }
}
