using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class TelemetryHistoryRecordConfiguration : IEntityTypeConfiguration<TelemetryHistoryRecord>
    {
        public void Configure(EntityTypeBuilder<TelemetryHistoryRecord> builder)
        {
            builder.ToTable("TelemetryHistoryRecords");

            builder.HasKey(t => t.Id);

            builder.Property(t => t.WorkstationId)
                .IsRequired();

            builder.Property(t => t.OrganizationId);
            builder.Property(t => t.SiteId);

            builder.Property(t => t.PcId)
                .IsRequired()
                .HasMaxLength(100);

            builder.Property(t => t.SessionId)
                .HasMaxLength(100);

            builder.Property(t => t.ConnectionId)
                .HasMaxLength(100);

            builder.Property(t => t.Cpu)
                .IsRequired();

            builder.Property(t => t.Ram)
                .IsRequired();

            builder.Property(t => t.Uptime)
                .IsRequired();

            builder.Property(t => t.RunningGameName)
                .HasMaxLength(256);

            builder.Property(t => t.RunningGamePid);
            builder.Property(t => t.RunningGameCpu);
            builder.Property(t => t.RunningGameRam);
            builder.Property(t => t.RunningGameDuration);

            builder.Property(t => t.TotalLaunches)
                .IsRequired();

            builder.Property(t => t.TotalCrashes)
                .IsRequired();

            builder.Property(t => t.TotalRestarts)
                .IsRequired();

            builder.Property(t => t.ClientTimestamp)
                .IsRequired();

            builder.Property(t => t.ServerReceivedAt)
                .IsRequired();

            builder.Property(t => t.ProcessedAt)
                .IsRequired();

            // Indexes optimized for historical time-range queries and tenant isolation
            builder.HasIndex(t => new { t.WorkstationId, t.ServerReceivedAt });
            builder.HasIndex(t => new { t.SiteId, t.ServerReceivedAt });
            builder.HasIndex(t => new { t.OrganizationId, t.ServerReceivedAt });
            builder.HasIndex(t => t.ServerReceivedAt);
        }
    }
}
