using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class WorkstationSessionConfiguration : IEntityTypeConfiguration<WorkstationSession>
    {
        public void Configure(EntityTypeBuilder<WorkstationSession> builder)
        {
            builder.ToTable("WorkstationSessions");

            builder.HasKey(s => s.Id);

            builder.Property(s => s.WorkstationId)
                .IsRequired();

            builder.Property(s => s.GamerId)
                .IsRequired();

            builder.Property(s => s.StartTime)
                .IsRequired();

            builder.Property(s => s.EndTime);

            // Configure precise decimal precision for financial/monetary values
            builder.Property(s => s.RatePerHour)
                .HasPrecision(18, 4)
                .IsRequired();

            builder.Property(s => s.CurrentCost)
                .HasPrecision(18, 4)
                .IsRequired();

            builder.Property(s => s.RemainingCredits)
                .HasPrecision(18, 4)
                .IsRequired();

            builder.Property(s => s.BillingAmount)
                .HasPrecision(18, 4)
                .IsRequired();

            builder.Property(s => s.Currency)
                .IsRequired()
                .HasMaxLength(10)
                .HasDefaultValue("SAY");

            builder.Property(s => s.SessionState)
                .IsRequired()
                .HasMaxLength(50)
                .HasDefaultValue("Active");

            builder.Property(s => s.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();

            // Indexes for session queries
            builder.HasIndex(s => s.WorkstationId);
            builder.HasIndex(s => s.GamerId);
            builder.HasIndex(s => s.SessionState);
        }
    }
}
