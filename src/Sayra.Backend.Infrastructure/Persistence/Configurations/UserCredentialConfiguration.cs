using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class UserCredentialConfiguration : IEntityTypeConfiguration<UserCredential>
    {
        public void Configure(EntityTypeBuilder<UserCredential> builder)
        {
            builder.ToTable("UserCredentials");

            builder.HasKey(uc => uc.Id);

            builder.Property(uc => uc.UserEntityId)
                .IsRequired();

            builder.Property(uc => uc.PasswordHash)
                .IsRequired()
                .HasMaxLength(512);

            builder.Property(uc => uc.PasswordSalt)
                .IsRequired()
                .HasMaxLength(256);

            builder.Property(uc => uc.HashAlgorithm)
                .IsRequired()
                .HasMaxLength(50);

            builder.Property(uc => uc.HashParameters)
                .HasMaxLength(1000);

            builder.HasIndex(uc => uc.UserEntityId)
                .IsUnique()
                .HasDatabaseName("IX_UserCredentials_UserEntityId");

            builder.HasOne(uc => uc.User)
                .WithOne()
                .HasForeignKey<UserCredential>(uc => uc.UserEntityId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
