using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class WorkstationGroupMemberConfiguration : IEntityTypeConfiguration<WorkstationGroupMember>
    {
        public void Configure(EntityTypeBuilder<WorkstationGroupMember> builder)
        {
            builder.ToTable("workstation_group_members");

            builder.HasKey(m => new { m.WorkstationGroupId, m.WorkstationId });

            builder.Property(m => m.JoinedAt)
                .IsRequired();

            builder.HasIndex(m => m.WorkstationId);
        }
    }
}
