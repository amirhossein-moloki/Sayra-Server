using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
    {
        public void Configure(EntityTypeBuilder<AuditEvent> builder)
        {
            builder.ToTable("AuditEvents");

            builder.HasKey(a => a.Id);

            // Globally unique EventId for offline-event idempotency and duplicate checking
            builder.Property(a => a.EventId)
                .IsRequired();

            builder.Property(a => a.EventType)
                .IsRequired()
                .HasMaxLength(100);

            builder.Property(a => a.EventVersion)
                .IsRequired();

            builder.Property(a => a.WorkstationId);
            builder.Property(a => a.SessionId);

            builder.Property(a => a.CorrelationId)
                .HasMaxLength(100);

            builder.Property(a => a.TraceId)
                .HasMaxLength(100);

            // Native JSONB representation in PostgreSQL for structured payload
            builder.Property(a => a.Payload)
                .HasColumnType("jsonb")
                .IsRequired()
                .HasDefaultValue("{}");

            builder.Property(a => a.Priority)
                .IsRequired()
                .HasDefaultValue(0);

            builder.Property(a => a.Timestamp)
                .IsRequired();

            // Explicit database constraints and indexes
            builder.HasIndex(a => a.EventId)
                .IsUnique(); // Enforce database-level uniqueness for retransmitted offline events

            builder.HasIndex(a => a.WorkstationId);
            builder.HasIndex(a => a.SessionId);
            builder.HasIndex(a => a.Timestamp);
        }
    }
}
