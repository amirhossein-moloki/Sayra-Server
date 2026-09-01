using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class RemoteCommandConfiguration : IEntityTypeConfiguration<RemoteCommand>
    {
        public void Configure(EntityTypeBuilder<RemoteCommand> builder)
        {
            builder.ToTable("remote_commands");

            builder.HasKey(c => c.Id);

            builder.Property(c => c.CommandId)
                .IsRequired()
                .HasMaxLength(128);

            builder.HasIndex(c => c.CommandId)
                .IsUnique();

            builder.Property(c => c.CommandType)
                .IsRequired()
                .HasMaxLength(64);

            builder.Property(c => c.TargetWorkstationId)
                .IsRequired();

            builder.Property(c => c.TargetPcId)
                .IsRequired()
                .HasMaxLength(64);

            builder.HasIndex(c => c.TargetPcId);

            builder.HasIndex(c => new { c.TargetWorkstationId, c.Status, c.CreatedAt });

            builder.Property(c => c.TargetConnectionId)
                .HasMaxLength(128);

            builder.Property(c => c.RequestedBy)
                .IsRequired()
                .HasMaxLength(128);

            builder.Property(c => c.Status)
                .IsRequired()
                .HasMaxLength(32);

            builder.Property(c => c.CorrelationId)
                .IsRequired()
                .HasMaxLength(128);

            builder.Property(c => c.ErrorCode)
                .HasMaxLength(64);

            builder.Property(c => c.FailureReason)
                .HasMaxLength(512);

            builder.Property(c => c.RowVersion)
                .IsRowVersion();

            builder.Ignore(c => c.DomainEvents);
        }
    }
}
