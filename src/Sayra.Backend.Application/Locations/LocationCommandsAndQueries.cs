using System;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Application.Locations
{
    public class CreateSiteCommand : ICommand<Site>
    {
        public Guid OrganizationId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Timezone { get; set; } = "UTC";
    }

    public class DeactivateSiteCommand : ICommand<Site>
    {
        public Guid SiteId { get; set; }
    }

    public class GetSiteQuery : IQuery<Site>
    {
        public Guid SiteId { get; set; }
    }

    public class CreateZoneCommand : ICommand<Zone>
    {
        public Guid SiteId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
    }

    public class DeactivateZoneCommand : ICommand<Zone>
    {
        public Guid ZoneId { get; set; }
    }

    public class GetZoneQuery : IQuery<Zone>
    {
        public Guid ZoneId { get; set; }
    }
}
