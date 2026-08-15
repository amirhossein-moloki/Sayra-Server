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

            builder.Property(w => w.OrganizationEntityId);

            builder.Property(w => w.SiteEntityId);

            builder.Property(w => w.ZoneEntityId);

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

            builder.Property(w => w.IsDeactivated)
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

            // Foreign keys
            builder.HasOne<Organization>()
                .WithMany()
                .HasForeignKey(w => w.OrganizationEntityId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne<Site>()
                .WithMany()
                .HasForeignKey(w => w.SiteEntityId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne<Zone>()
                .WithMany()
                .HasForeignKey(w => w.ZoneEntityId)
                .OnDelete(DeleteBehavior.Restrict);

            // Indexes/Constraints mapping
            builder.HasIndex(w => w.PcId).IsUnique();
            builder.HasIndex(w => w.MacAddress).IsUnique();
            builder.HasIndex(w => w.SiteId);
            builder.HasIndex(w => w.OrganizationEntityId);
            builder.HasIndex(w => w.SiteEntityId);
            builder.HasIndex(w => w.ZoneEntityId);
            builder.HasIndex(w => w.Status);
            builder.HasIndex(w => w.LastSeen);
        }
    }
}
