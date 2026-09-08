using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class IncidentConfiguration : IEntityTypeConfiguration<Incident>
    {
        public void Configure(EntityTypeBuilder<Incident> builder)
        {
            builder.ToTable("Incidents");

            builder.HasKey(i => i.Id);

            builder.Property(i => i.Fingerprint)
                .IsRequired()
                .HasMaxLength(128);

            builder.Property(i => i.RuleCode)
                .IsRequired()
                .HasMaxLength(64);

            builder.Property(i => i.OrganizationId)
                .IsRequired();

            builder.Property(i => i.SiteId);
            builder.Property(i => i.WorkstationId);

            builder.Property(i => i.PcId)
                .IsRequired()
                .HasMaxLength(64);

            builder.Property(i => i.Severity)
                .IsRequired();

            builder.Property(i => i.LifecycleState)
                .IsRequired();

            builder.Property(i => i.FirstTriggeredAtUtc)
                .IsRequired();

            builder.Property(i => i.FiringAtUtc);
            builder.Property(i => i.LastObservedAtUtc)
                .IsRequired();

            builder.Property(i => i.ResolvedAtUtc);

            builder.Property(i => i.ReasonCode)
                .HasMaxLength(64);

            builder.Property(i => i.Source)
                .HasMaxLength(64);

            builder.Property(i => i.Title)
                .HasMaxLength(256);

            builder.Property(i => i.Description)
                .HasMaxLength(1024);

            builder.Property(i => i.TriggerEvidence)
                .IsRequired();

            builder.Property(i => i.RecoveryEvidence);

            builder.Property(i => i.PolicyVersion)
                .HasMaxLength(64);

            builder.Property(i => i.IsSuppressed)
                .IsRequired();

            builder.Property(i => i.SuppressionReason)
                .HasMaxLength(256);

            builder.Property(i => i.ObservationCount)
                .IsRequired();

            builder.Property(i => i.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();

            builder.HasIndex(i => i.Fingerprint);
            builder.HasIndex(i => new { i.OrganizationId, i.LifecycleState });
            builder.HasIndex(i => new { i.SiteId, i.LifecycleState });
            builder.HasIndex(i => new { i.PcId, i.LifecycleState });
            builder.HasIndex(i => i.FirstTriggeredAtUtc);
        }
    }
}
