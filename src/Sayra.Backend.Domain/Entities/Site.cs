using System;

namespace Sayra.Backend.Domain
{
    public class Site : BaseEntity
    {
        public string SiteId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }
}
