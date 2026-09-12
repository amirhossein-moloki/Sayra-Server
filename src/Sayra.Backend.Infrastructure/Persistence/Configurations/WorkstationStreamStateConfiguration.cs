using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class WorkstationStreamStateConfiguration : IEntityTypeConfiguration<WorkstationStreamState>
    {
        public void Configure(EntityTypeBuilder<WorkstationStreamState> builder)
        {
            builder.ToTable("WorkstationStreamStates");

            builder.HasKey(e => e.Id);

            builder.Property(e => e.ClientId)
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(e => e.WorkstationId);

            builder.Property(e => e.LastSequenceNumber)
                .IsRequired();

            builder.Property(e => e.LastProcessedAt)
                .IsRequired();

            builder.Property(e => e.RowVersion)
                .IsRowVersion();

            builder.HasIndex(e => e.ClientId)
                .IsUnique();
        }
    }
}
