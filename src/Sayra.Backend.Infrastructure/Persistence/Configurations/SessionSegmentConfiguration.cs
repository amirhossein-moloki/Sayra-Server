using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class SessionSegmentConfiguration : IEntityTypeConfiguration<SessionSegment>
    {
        public void Configure(EntityTypeBuilder<SessionSegment> builder)
        {
            builder.ToTable("session_segments");

            builder.HasKey(s => s.Id);

            builder.Ignore(s => s.SessionSegmentId);

            builder.Property(s => s.SessionId)
                .IsRequired();

            builder.Property(s => s.Type)
                .IsRequired()
                .HasMaxLength(20);

            builder.Property(s => s.StartedAtUtc)
                .IsRequired();

            builder.Property(s => s.EndedAtUtc)
                .IsRequired(false);

            builder.HasIndex(s => s.SessionId)
                .HasDatabaseName("IX_session_segments_SessionId");

            builder.HasIndex(s => new { s.SessionId, s.StartedAtUtc })
                .HasDatabaseName("IX_session_segments_SessionId_StartedAtUtc");

            builder.HasIndex(s => new { s.SessionId, s.Type })
                .HasDatabaseName("IX_session_segments_SessionId_Type");

            builder.HasOne<Session>()
                .WithMany()
                .HasForeignKey(s => s.SessionId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
