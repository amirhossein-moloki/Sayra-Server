using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class GamerCredentialConfiguration : IEntityTypeConfiguration<GamerCredential>
    {
        public void Configure(EntityTypeBuilder<GamerCredential> builder)
        {
            builder.ToTable("GamerCredentials");

            builder.HasKey(c => c.Id);

            builder.Property(c => c.GamerEntityId)
                .IsRequired();

            builder.Property(c => c.CredentialType)
                .IsRequired()
                .HasMaxLength(50);

            builder.Property(c => c.PasswordHash)
                .IsRequired()
                .HasMaxLength(512);

            builder.Property(c => c.PasswordSalt)
                .IsRequired()
                .HasMaxLength(256);

            builder.Property(c => c.HashAlgorithm)
                .IsRequired()
                .HasMaxLength(50);

            builder.HasIndex(c => c.GamerEntityId)
                .IsUnique()
                .HasDatabaseName("IX_GamerCredentials_GamerEntityId");

            builder.HasOne<Gamer>()
                .WithMany()
                .HasForeignKey(c => c.GamerEntityId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
