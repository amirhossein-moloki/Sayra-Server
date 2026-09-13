using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class DeadLetterEventConfiguration : IEntityTypeConfiguration<DeadLetterEvent>
    {
        public void Configure(EntityTypeBuilder<DeadLetterEvent> builder)
        {
            builder.ToTable("DeadLetterEvents");

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

            builder.Property(e => e.SiteId)
                .HasMaxLength(100);

            builder.Property(e => e.OrganizationId);

            builder.Property(e => e.EventType)
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(e => e.SequenceNumber)
                .IsRequired();

            builder.Property(e => e.ReliabilityClass)
                .HasMaxLength(50)
                .IsRequired()
                .HasDefaultValue("NORMAL");

            builder.Property(e => e.Payload)
                .IsRequired();

            builder.Property(e => e.FailureCode)
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(e => e.FailureReason)
                .HasMaxLength(2000)
                .IsRequired();

            builder.Property(e => e.RetryCount)
                .IsRequired();

            builder.Property(e => e.FirstSeenAt)
                .IsRequired();

            builder.Property(e => e.LastAttemptAt)
                .IsRequired();

            builder.Property(e => e.DeadLetteredAt)
                .IsRequired();

            builder.Property(e => e.CorrelationId)
                .HasMaxLength(100);

            builder.Property(e => e.ProcessingStatus)
                .HasMaxLength(50)
                .IsRequired()
                .HasDefaultValue(DeadLetterStatus.DeadLetter);

            builder.Property(e => e.RecoveredAt);

            builder.Property(e => e.RecoveredBy)
                .HasMaxLength(200);

            builder.HasIndex(e => e.EventId)
                .IsUnique();

            builder.HasIndex(e => e.ClientId);
            builder.HasIndex(e => e.SiteId);
            builder.HasIndex(e => e.DeadLetteredAt);
            builder.HasIndex(e => new { e.ProcessingStatus, e.DeadLetteredAt });
        }
    }
}
