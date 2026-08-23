using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class LoginAttemptConfiguration : IEntityTypeConfiguration<LoginAttempt>
    {
        public void Configure(EntityTypeBuilder<LoginAttempt> builder)
        {
            builder.ToTable("login_attempts");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.UsernameIdentifier)
                .IsRequired()
                .HasMaxLength(256);

            builder.Property(x => x.IpAddress)
                .HasMaxLength(64);

            builder.Property(x => x.DeviceId)
                .HasMaxLength(100);

            builder.Property(x => x.FailureReason)
                .HasMaxLength(500);

            builder.Property(x => x.AttemptCount)
                .IsRequired()
                .HasDefaultValue(0);

            builder.Property(x => x.CreatedAt)
                .IsRequired();

            builder.Property(x => x.LastAttemptAt)
                .IsRequired();

            builder.HasIndex(x => x.UsernameIdentifier)
                .HasDatabaseName("IX_login_attempts_UsernameIdentifier");

            builder.HasIndex(x => x.IpAddress)
                .HasDatabaseName("IX_login_attempts_IpAddress");

            builder.HasIndex(x => x.CreatedAt)
                .HasDatabaseName("IX_login_attempts_CreatedAt");

            builder.HasIndex(x => x.UserId)
                .HasDatabaseName("IX_login_attempts_UserId");
        }
    }
}
