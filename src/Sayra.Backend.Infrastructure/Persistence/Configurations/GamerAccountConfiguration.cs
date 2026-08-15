using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class GamerAccountConfiguration : IEntityTypeConfiguration<GamerAccount>
    {
        public void Configure(EntityTypeBuilder<GamerAccount> builder)
        {
            builder.ToTable("GamerAccounts");

            builder.HasKey(a => a.Id);

            builder.Property(a => a.GamerEntityId)
                .IsRequired();

            builder.Property(a => a.AccountNumber)
                .IsRequired()
                .HasMaxLength(50);

            builder.Property(a => a.Status)
                .IsRequired()
                .HasMaxLength(20);

            builder.Property(a => a.Currency)
                .IsRequired()
                .HasMaxLength(10)
                .HasDefaultValue("SAY");

            builder.Property(a => a.Balance)
                .HasPrecision(18, 4)
                .IsRequired();

            builder.Property(a => a.BonusBalance)
                .HasPrecision(18, 4)
                .IsRequired();

            builder.Property(a => a.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();

            builder.HasIndex(a => a.AccountNumber)
                .IsUnique()
                .HasDatabaseName("IX_GamerAccounts_AccountNumber");

            builder.HasIndex(a => a.GamerEntityId)
                .IsUnique()
                .HasDatabaseName("IX_GamerAccounts_GamerEntityId");

            builder.HasOne<Gamer>()
                .WithMany()
                .HasForeignKey(a => a.GamerEntityId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
