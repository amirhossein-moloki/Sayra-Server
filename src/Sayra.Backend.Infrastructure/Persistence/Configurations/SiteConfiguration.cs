using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class SiteConfiguration : IEntityTypeConfiguration<Site>
    {
        public void Configure(EntityTypeBuilder<Site> builder)
        {
            builder.ToTable("Sites");

            builder.HasKey(s => s.Id);

            builder.Property(s => s.SiteId)
                .IsRequired()
                .HasMaxLength(100);

            builder.Property(s => s.Name)
                .IsRequired()
                .HasMaxLength(100);

            builder.HasIndex(s => s.SiteId).IsUnique();

            // Seed default sites for compatibility and lookup
            builder.HasData(
                new Site
                {
                    SiteId = "SITE-A",
                    Name = "Site A"
                }.WithDeterministicId(Guid.Parse("6a9254d3-1823-45a4-966a-1cc12df6992d")),
                new Site
                {
                    SiteId = "SITE-ALPHA",
                    Name = "Site Alpha"
                }.WithDeterministicId(Guid.Parse("bce0cf94-4d1a-45c5-9f5b-16629dfc29f2")),
                new Site
                {
                    SiteId = "SITE-BETA",
                    Name = "Site Beta"
                }.WithDeterministicId(Guid.Parse("7c180905-1a8c-4fdf-973a-4be3a30fc39c"))
            );
        }
    }

    public static class SiteSeedingExtensions
    {
        public static Site WithDeterministicId(this Site site, Guid id)
        {
            // Reflection or property setter since Id might have a protected setter
            var idProp = typeof(BaseEntity).GetProperty("Id");
            idProp?.SetValue(site, id);
            return site;
        }
    }
}
