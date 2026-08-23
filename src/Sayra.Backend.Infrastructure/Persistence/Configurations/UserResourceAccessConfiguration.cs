using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class UserResourceAccessConfiguration : IEntityTypeConfiguration<UserResourceAccess>
    {
        public void Configure(EntityTypeBuilder<UserResourceAccess> builder)
        {
            builder.ToTable("user_resource_accesses");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.ResourceType)
                .IsRequired()
                .HasMaxLength(100);

            builder.Property(x => x.IsGranted)
                .IsRequired();

            builder.Property(x => x.Status)
                .IsRequired()
                .HasMaxLength(50)
                .HasDefaultValue("Active");

            builder.Property(x => x.CreatedAt)
                .IsRequired();

            builder.Property(x => x.UpdatedAt);

            builder.HasIndex(x => new { x.UserEntityId, x.ResourceType, x.ResourceId })
                .HasDatabaseName("IX_UserResourceAccesses_User_Type_Resource");

            builder.HasIndex(x => new { x.RoleId, x.ResourceType, x.ResourceId })
                .HasDatabaseName("IX_UserResourceAccesses_Role_Type_Resource");
        }
    }
}
