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

            builder.Property(w => w.PcId)
                .IsRequired()
                .HasMaxLength(100);

            builder.Property(w => w.SiteId)
                .IsRequired()
                .HasMaxLength(100);

            builder.Property(w => w.Hostname)
                .IsRequired()
                .HasMaxLength(100);

            builder.Property(w => w.IpAddress)
                .IsRequired()
                .HasMaxLength(50);

            builder.Property(w => w.MacAddress)
                .IsRequired()
                .HasMaxLength(50);

            builder.Property(w => w.ClientVersion)
                .HasMaxLength(50);

            builder.Property(w => w.OsVersion)
                .HasMaxLength(50);

            builder.Property(w => w.Status)
                .IsRequired()
                .HasMaxLength(50)
                .HasDefaultValue("OFFLINE");

            builder.Property(w => w.LastSeen)
                .IsRequired();

            builder.Property(w => w.IsDisabled)
                .IsRequired()
                .HasDefaultValue(false);

            builder.Property(w => w.IsProvisioned)
                .IsRequired()
                .HasDefaultValue(false);

            builder.Property(w => w.ProvisionedAt);

            builder.Property(w => w.VerificationPublicKey)
                .HasColumnType("bytea");

            builder.Property(w => w.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();

            // Indexes/Constraints mapping
            builder.HasIndex(w => w.PcId).IsUnique();
            builder.HasIndex(w => w.MacAddress).IsUnique();
            builder.HasIndex(w => w.SiteId);
            builder.HasIndex(w => w.Status);
            builder.HasIndex(w => w.LastSeen);
        }
    }
}
