using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class AuthenticationSessionConfiguration : IEntityTypeConfiguration<AuthenticationSession>
    {
        public void Configure(EntityTypeBuilder<AuthenticationSession> builder)
        {
            builder.ToTable("AuthenticationSessions");

            builder.HasKey(s => s.Id);

            builder.Property(s => s.SessionToken)
                .IsRequired()
                .HasMaxLength(128);

            builder.HasIndex(s => s.SessionToken)
                .IsUnique();

            builder.Property(s => s.PcId)
                .HasMaxLength(64);

            builder.Property(s => s.Status)
                .IsRequired()
                .HasMaxLength(32)
                .HasDefaultValue(AuthenticationSession.StatusActive);

            builder.Property(s => s.RevocationReason)
                .HasMaxLength(256);

            builder.Property(s => s.CreatedBy)
                .HasMaxLength(128);

            builder.Property(s => s.IpAddress)
                .HasMaxLength(64);

            builder.Property(s => s.UserAgent)
                .HasMaxLength(512);

            builder.HasIndex(s => s.UserId);
            builder.HasIndex(s => s.GamerId);
            builder.HasIndex(s => s.PcId);
            builder.HasIndex(s => s.Status);
            builder.HasIndex(s => s.ExpiresAt);
            builder.HasIndex(s => s.RevokedAt);
        }
    }
}
