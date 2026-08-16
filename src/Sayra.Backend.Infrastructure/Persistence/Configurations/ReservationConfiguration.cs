using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
    {
        public void Configure(EntityTypeBuilder<Reservation> builder)
        {
            builder.ToTable("Reservations");

            builder.HasKey(r => r.Id);

            builder.Ignore(r => r.ReservationId);

            builder.Property(r => r.OrganizationId)
                .IsRequired();

            builder.Property(r => r.SiteId)
                .IsRequired();

            builder.Property(r => r.GamerId)
                .IsRequired();

            builder.Property(r => r.WorkstationId)
                .IsRequired(false);

            builder.Property(r => r.ZoneId)
                .IsRequired(false);

            builder.Property(r => r.StartTimeUtc)
                .IsRequired();

            builder.Property(r => r.EndTimeUtc)
                .IsRequired();

            builder.Property(r => r.Status)
                .IsRequired()
                .HasMaxLength(20);

            builder.Property(r => r.ReservedAmount)
                .HasPrecision(18, 2);

            builder.HasIndex(r => new { r.GamerId, r.Status })
                .HasDatabaseName("IX_Reservations_GamerId_Status");

            builder.HasIndex(r => new { r.SiteId, r.StartTimeUtc, r.EndTimeUtc })
                .HasDatabaseName("IX_Reservations_SiteId_StartTimeUtc_EndTimeUtc");

            builder.HasIndex(r => new { r.WorkstationId, r.StartTimeUtc, r.EndTimeUtc })
                .HasDatabaseName("IX_Reservations_WorkstationId_StartTimeUtc_EndTimeUtc");

            builder.HasIndex(r => r.Status)
                .HasDatabaseName("IX_Reservations_Status");

            builder.HasOne<Organization>()
                .WithMany()
                .HasForeignKey(r => r.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne<Site>()
                .WithMany()
                .HasForeignKey(r => r.SiteId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne<Gamer>()
                .WithMany()
                .HasForeignKey(r => r.GamerId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne<Workstation>()
                .WithMany()
                .HasForeignKey(r => r.WorkstationId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);

            builder.HasOne<Zone>()
                .WithMany()
                .HasForeignKey(r => r.ZoneId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
        }
    }
}
