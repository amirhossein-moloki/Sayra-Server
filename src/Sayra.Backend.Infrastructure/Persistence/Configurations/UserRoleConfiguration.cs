using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class UserRoleConfiguration : IEntityTypeConfiguration<UserRoleEntity>
    {
        public void Configure(EntityTypeBuilder<UserRoleEntity> builder)
        {
            builder.ToTable("UserRoles");

            builder.HasKey(ur => ur.Id);

            builder.Property(ur => ur.UserEntityId)
                .IsRequired();

            builder.Property(ur => ur.RoleId)
                .IsRequired();

            builder.HasIndex(ur => new { ur.UserEntityId, ur.RoleId })
                .IsUnique()
                .HasDatabaseName("IX_UserRoles_UserEntityId_RoleId");

            builder.HasOne(ur => ur.User)
                .WithMany()
                .HasForeignKey(ur => ur.UserEntityId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(ur => ur.Role)
                .WithMany()
                .HasForeignKey(ur => ur.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
