using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class TelemetryAggregateRecordConfiguration : IEntityTypeConfiguration<TelemetryAggregateRecord>
    {
        public void Configure(EntityTypeBuilder<TelemetryAggregateRecord> builder)
        {
            builder.ToTable("TelemetryAggregateRecords");

            builder.HasKey(t => t.Id);

            builder.Property(t => t.WorkstationId)
                .IsRequired();

            builder.Property(t => t.OrganizationId);
            builder.Property(t => t.SiteId);

            builder.Property(t => t.PcId)
                .IsRequired()
                .HasMaxLength(100);

            builder.Property(t => t.Granularity)
                .IsRequired()
                .HasMaxLength(10);

            builder.Property(t => t.WindowStart)
                .IsRequired();

            builder.Property(t => t.WindowEnd)
                .IsRequired();

            builder.Property(t => t.SampleCount)
                .IsRequired();

            builder.Property(t => t.IsPartial)
                .IsRequired();

            builder.Property(t => t.PrimaryGameName)
                .HasMaxLength(256);

            // Composite Unique Index for Idempotency and Deduplication
            builder.HasIndex(t => new { t.WorkstationId, t.Granularity, t.WindowStart })
                .IsUnique();

            // Range Query Indexes for Workstation, Site, and Organization
            builder.HasIndex(t => new { t.WorkstationId, t.Granularity, t.WindowStart, t.WindowEnd });
            builder.HasIndex(t => new { t.SiteId, t.Granularity, t.WindowStart, t.WindowEnd });
            builder.HasIndex(t => new { t.OrganizationId, t.Granularity, t.WindowStart, t.WindowEnd });
            builder.HasIndex(t => new { t.Granularity, t.WindowStart });
        }
    }
}
