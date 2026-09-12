using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class ProcessedEventConfiguration : IEntityTypeConfiguration<ProcessedEvent>
    {
        public void Configure(EntityTypeBuilder<ProcessedEvent> builder)
        {
            builder.ToTable("ProcessedEvents");

            builder.HasKey(e => e.Id);

            builder.Property(e => e.EventId)
                .IsRequired();

            builder.Property(e => e.BatchId)
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(e => e.ClientId)
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(e => e.WorkstationId);

            builder.Property(e => e.EventType)
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(e => e.SequenceNumber)
                .IsRequired();

            builder.Property(e => e.ReliabilityClass)
                .HasMaxLength(50)
                .IsRequired()
                .HasDefaultValue("NORMAL");

            builder.Property(e => e.OrderingStatus)
                .HasMaxLength(50)
                .IsRequired()
                .HasDefaultValue("UNORDERED");

            builder.Property(e => e.ProcessingStatus)
                .HasMaxLength(50)
                .IsRequired()
                .HasDefaultValue("ACCEPTED");

            builder.Property(e => e.PayloadHash)
                .HasMaxLength(64)
                .IsRequired();

            builder.Property(e => e.ReasonCode)
                .HasMaxLength(100);

            builder.Property(e => e.ErrorMessage)
                .HasMaxLength(2000);

            builder.Property(e => e.ConflictMetadata)
                .HasMaxLength(2000);

            builder.Property(e => e.OccurredAt);

            builder.Property(e => e.FirstReceivedAt)
                .IsRequired();

            builder.Property(e => e.LastReceivedAt)
                .IsRequired();

            builder.Property(e => e.ProcessedAt)
                .IsRequired();

            builder.Property(e => e.ReconciledAt);

            // Explicit database uniqueness constraint on EventId
            builder.HasIndex(e => e.EventId)
                .IsUnique();

            builder.HasIndex(e => e.BatchId);
            builder.HasIndex(e => e.ClientId);
            builder.HasIndex(e => e.FirstReceivedAt);
            builder.HasIndex(e => new { e.ClientId, e.ProcessingStatus, e.SequenceNumber });
        }
    }
}
