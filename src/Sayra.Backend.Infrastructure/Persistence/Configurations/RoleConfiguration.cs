using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class RoleConfiguration : IEntityTypeConfiguration<Role>
    {
        public void Configure(EntityTypeBuilder<Role> builder)
        {
            builder.ToTable("Roles");

            builder.HasKey(r => r.Id);

            builder.Property(r => r.Code)
                .IsRequired()
                .HasMaxLength(50);

            builder.Property(r => r.Name)
                .IsRequired()
                .HasMaxLength(100);

            builder.Property(r => r.Description)
                .HasMaxLength(256);

            builder.Property(r => r.IsSystemRole)
                .IsRequired();

            builder.HasIndex(r => r.Code)
                .IsUnique()
                .HasDatabaseName("IX_Roles_Code");
        }
    }
}
