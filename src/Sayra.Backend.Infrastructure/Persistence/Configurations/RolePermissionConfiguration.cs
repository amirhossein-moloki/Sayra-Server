using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
    {
        public void Configure(EntityTypeBuilder<RolePermission> builder)
        {
            builder.ToTable("RolePermissions");

            builder.HasKey(rp => rp.Id);

            builder.Property(rp => rp.RoleId)
                .IsRequired();

            builder.Property(rp => rp.PermissionId)
                .IsRequired();

            builder.HasIndex(rp => new { rp.RoleId, rp.PermissionId })
                .IsUnique()
                .HasDatabaseName("IX_RolePermissions_RoleId_PermissionId");

            builder.HasOne(rp => rp.Role)
                .WithMany()
                .HasForeignKey(rp => rp.RoleId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(rp => rp.Permission)
                .WithMany()
                .HasForeignKey(rp => rp.PermissionId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
