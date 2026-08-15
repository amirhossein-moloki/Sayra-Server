using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class GamerConfiguration : IEntityTypeConfiguration<Gamer>
    {
        public void Configure(EntityTypeBuilder<Gamer> builder)
        {
            builder.ToTable("Gamers");

            builder.HasKey(g => g.Id);

            builder.Property(g => g.GamerId)
                .IsRequired()
                .HasMaxLength(50);

            builder.Property(g => g.Username)
                .IsRequired()
                .HasMaxLength(50);

            builder.Property(g => g.Email)
                .IsRequired()
                .HasMaxLength(256);

            builder.Property(g => g.PhoneNumber)
                .HasMaxLength(30);

            builder.Property(g => g.FirstName)
                .HasMaxLength(100);

            builder.Property(g => g.LastName)
                .HasMaxLength(100);

            builder.Property(g => g.Status)
                .IsRequired()
                .HasMaxLength(20);

            builder.HasIndex(g => g.GamerId)
                .IsUnique()
                .HasDatabaseName("IX_Gamers_GamerId");

            builder.HasIndex(g => g.Username)
                .IsUnique()
                .HasDatabaseName("IX_Gamers_Username");

            builder.HasIndex(g => g.Email)
                .IsUnique()
                .HasDatabaseName("IX_Gamers_Email");

            builder.HasOne<Organization>()
                .WithMany()
                .HasForeignKey(g => g.OrganizationEntityId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);

            builder.HasOne<Site>()
                .WithMany()
                .HasForeignKey(g => g.SiteEntityId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
        }
    }
}
