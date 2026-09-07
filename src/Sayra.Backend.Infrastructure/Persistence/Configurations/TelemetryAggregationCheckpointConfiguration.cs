using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class TelemetryAggregationCheckpointConfiguration : IEntityTypeConfiguration<TelemetryAggregationCheckpoint>
    {
        public void Configure(EntityTypeBuilder<TelemetryAggregationCheckpoint> builder)
        {
            builder.ToTable("TelemetryAggregationCheckpoints");

            builder.HasKey(t => t.Id);

            builder.Property(t => t.Granularity)
                .IsRequired()
                .HasMaxLength(10);

            builder.Property(t => t.LastProcessedWindowEnd)
                .IsRequired();

            builder.Property(t => t.LastProcessedServerTimestamp)
                .IsRequired();

            builder.Property(t => t.RecordsProcessed)
                .IsRequired();

            builder.HasIndex(t => t.Granularity)
                .IsUnique();
        }
    }
}
