using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class PermissionConfiguration : IEntityTypeConfiguration<Permission>
    {
        public void Configure(EntityTypeBuilder<Permission> builder)
        {
            builder.ToTable("Permissions");

            builder.HasKey(p => p.Id);

            builder.Property(p => p.Code)
                .IsRequired()
                .HasMaxLength(100);

            builder.Property(p => p.Name)
                .IsRequired()
                .HasMaxLength(100);

            builder.Property(p => p.Category)
                .IsRequired()
                .HasMaxLength(50);

            builder.Property(p => p.Description)
                .HasMaxLength(256);

            builder.Property(p => p.Status)
                .IsRequired()
                .HasMaxLength(50)
                .HasDefaultValue("Active");

            builder.HasIndex(p => p.Code)
                .IsUnique()
                .HasDatabaseName("IX_Permissions_Code");
        }
    }
}
