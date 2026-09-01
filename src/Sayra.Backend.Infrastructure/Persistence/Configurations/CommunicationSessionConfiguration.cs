using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class CommunicationSessionConfiguration : IEntityTypeConfiguration<CommunicationSession>
    {
        public void Configure(EntityTypeBuilder<CommunicationSession> builder)
        {
            builder.ToTable("CommunicationSessions");

            builder.HasKey(s => s.Id);

            builder.Property(s => s.ConnectionId)
                .IsRequired()
                .HasMaxLength(128);

            builder.HasIndex(s => s.ConnectionId)
                .IsUnique();

            builder.Property(s => s.PcId)
                .HasMaxLength(64);

            builder.HasIndex(s => s.PcId);

            builder.HasIndex(s => s.WorkstationId);

            builder.Property(s => s.State)
                .IsRequired()
                .HasConversion<string>()
                .HasMaxLength(32);

            builder.HasIndex(s => s.State);

            builder.Property(s => s.HeartbeatStatus)
                .IsRequired()
                .HasConversion<string>()
                .HasMaxLength(32);

            builder.Property(s => s.DisconnectReason)
                .HasMaxLength(256);

            builder.Property(s => s.RemoteIpAddress)
                .HasMaxLength(64);

            builder.Property(s => s.Hostname)
                .HasMaxLength(128);

            builder.Ignore(s => s.TypedConnectionId);
            builder.Ignore(s => s.SessionId);
            builder.Ignore(s => s.HeartbeatState);
            builder.Ignore(s => s.IsActive);
            builder.Ignore(s => s.IsTerminated);
            builder.Ignore(s => s.DomainEvents);
        }
    }
}
