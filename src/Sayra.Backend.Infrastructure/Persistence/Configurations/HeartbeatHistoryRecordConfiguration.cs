using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class HeartbeatHistoryRecordConfiguration : IEntityTypeConfiguration<HeartbeatHistoryRecord>
    {
        public void Configure(EntityTypeBuilder<HeartbeatHistoryRecord> builder)
        {
            builder.ToTable("HeartbeatHistoryRecords");

            builder.HasKey(h => h.Id);

            builder.Property(h => h.WorkstationId)
                .IsRequired();

            builder.Property(h => h.OrganizationId);
            builder.Property(h => h.SiteId);

            builder.Property(h => h.PcId)
                .IsRequired()
                .HasMaxLength(100);

            builder.Property(h => h.ConnectionId)
                .HasMaxLength(100);

            builder.Property(h => h.ClientTimestamp)
                .IsRequired();

            builder.Property(h => h.ServerReceivedAt)
                .IsRequired();

            builder.Property(h => h.ProcessedAt)
                .IsRequired();

            // Compound index for liveness query filtering
            builder.HasIndex(h => new { h.WorkstationId, h.ServerReceivedAt });
            builder.HasIndex(h => h.ServerReceivedAt);
        }
    }
}
