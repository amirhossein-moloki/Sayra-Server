using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class SecurityEventConfiguration : IEntityTypeConfiguration<SecurityEvent>
    {
        public void Configure(EntityTypeBuilder<SecurityEvent> builder)
        {
            builder.ToTable("security_events");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.EventType)
                .IsRequired()
                .HasMaxLength(100);

            builder.Property(x => x.ActorType)
                .HasMaxLength(50);

            builder.Property(x => x.DeviceId)
                .HasMaxLength(100);

            builder.Property(x => x.ResourceType)
                .HasMaxLength(100);

            builder.Property(x => x.Action)
                .HasMaxLength(100);

            builder.Property(x => x.Result)
                .IsRequired()
                .HasMaxLength(50);

            builder.Property(x => x.FailureReason)
                .HasMaxLength(1000);

            builder.Property(x => x.CorrelationId)
                .HasMaxLength(100);

            builder.Property(x => x.TraceId)
                .HasMaxLength(100);

            builder.Property(x => x.CreatedAt)
                .IsRequired();

            builder.HasIndex(x => x.EventType)
                .HasDatabaseName("IX_security_events_EventType");

            builder.HasIndex(x => x.ActorId)
                .HasDatabaseName("IX_security_events_ActorId");

            builder.HasIndex(x => x.DeviceId)
                .HasDatabaseName("IX_security_events_DeviceId");

            builder.HasIndex(x => x.ResourceId)
                .HasDatabaseName("IX_security_events_ResourceId");

            builder.HasIndex(x => x.CreatedAt)
                .HasDatabaseName("IX_security_events_CreatedAt");

            builder.HasIndex(x => x.CorrelationId)
                .HasDatabaseName("IX_security_events_CorrelationId");
        }
    }
}
