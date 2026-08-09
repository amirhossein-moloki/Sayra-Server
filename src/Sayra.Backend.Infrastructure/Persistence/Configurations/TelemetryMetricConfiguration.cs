using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class TelemetryMetricConfiguration : IEntityTypeConfiguration<TelemetryMetric>
    {
        public void Configure(EntityTypeBuilder<TelemetryMetric> builder)
        {
            builder.ToTable("TelemetryMetrics");

            builder.HasKey(t => t.Id);

            builder.Property(t => t.WorkstationId)
                .IsRequired();

            builder.Property(t => t.MetricName)
                .IsRequired()
                .HasMaxLength(100);

            builder.Property(t => t.MetricValue)
                .IsRequired();

            builder.Property(t => t.DimensionJson)
                .HasColumnType("jsonb")
                .IsRequired()
                .HasDefaultValue("{}");

            builder.Property(t => t.Timestamp)
                .IsRequired();

            // Compound Index: extremely optimized for workstation-based queries and time-range queries
            builder.HasIndex(t => new { t.WorkstationId, t.Timestamp });

            // Single column index on Timestamp for general time-series queries
            builder.HasIndex(t => t.Timestamp);
        }
    }
}
