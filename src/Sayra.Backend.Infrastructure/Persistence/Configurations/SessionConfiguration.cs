using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class SessionConfiguration : IEntityTypeConfiguration<Session>
    {
        public void Configure(EntityTypeBuilder<Session> builder)
        {
            builder.ToTable("sessions");

            builder.HasKey(s => s.Id);

            builder.Ignore(s => s.SessionId);

            builder.Property(s => s.OrganizationId)
                .IsRequired();

            builder.Property(s => s.SiteId)
                .IsRequired();

            builder.Property(s => s.WorkstationId)
                .IsRequired();

            builder.Property(s => s.GamerId)
                .IsRequired();

            builder.Property(s => s.ReservationId)
                .IsRequired(false);

            builder.Property(s => s.PricingPlanId)
                .IsRequired(false);

            builder.Property(s => s.Status)
                .IsRequired()
                .HasMaxLength(20);

            builder.Property(s => s.StartedAt)
                .IsRequired();

            builder.Property(s => s.PausedAt)
                .IsRequired(false);

            builder.Property(s => s.EndedAt)
                .IsRequired(false);

            builder.Property(s => s.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();

            builder.HasIndex(s => s.Status)
                .HasDatabaseName("IX_sessions_Status");

            builder.HasIndex(s => new { s.WorkstationId, s.Status })
                .HasDatabaseName("IX_sessions_WorkstationId_Status");

            builder.HasIndex(s => new { s.GamerId, s.Status })
                .HasDatabaseName("IX_sessions_GamerId_Status");

            builder.HasIndex(s => new { s.ReservationId, s.Status })
                .HasDatabaseName("IX_sessions_ReservationId_Status");

            builder.HasOne<Organization>()
                .WithMany()
                .HasForeignKey(s => s.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne<Site>()
                .WithMany()
                .HasForeignKey(s => s.SiteId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne<Workstation>()
                .WithMany()
                .HasForeignKey(s => s.WorkstationId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne<Gamer>()
                .WithMany()
                .HasForeignKey(s => s.GamerId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne<Reservation>()
                .WithMany()
                .HasForeignKey(s => s.ReservationId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);

            builder.HasOne<PricingPlan>()
                .WithMany()
                .HasForeignKey(s => s.PricingPlanId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
        }
    }
}
