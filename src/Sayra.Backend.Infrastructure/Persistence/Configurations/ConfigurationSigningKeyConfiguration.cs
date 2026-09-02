using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class ConfigurationSigningKeyConfiguration : IEntityTypeConfiguration<ConfigurationSigningKey>
    {
        public void Configure(EntityTypeBuilder<ConfigurationSigningKey> builder)
        {
            builder.ToTable("ConfigurationSigningKeys");

            builder.HasKey(k => k.Id);

            builder.Property(k => k.KeyId)
                .IsRequired()
                .HasMaxLength(100);

            builder.Property(k => k.Algorithm)
                .IsRequired()
                .HasMaxLength(50)
                .HasDefaultValue("RSA-SHA256");

            builder.Property(k => k.PublicKeyPem)
                .IsRequired()
                .HasColumnType("text");

            builder.Property(k => k.Status)
                .HasConversion<string>()
                .IsRequired()
                .HasMaxLength(20);

            builder.Property(k => k.ValidFrom)
                .IsRequired();

            builder.Property(k => k.ValidTo)
                .IsRequired(false);

            builder.HasIndex(k => k.KeyId)
                .IsUnique();
        }
    }
}
