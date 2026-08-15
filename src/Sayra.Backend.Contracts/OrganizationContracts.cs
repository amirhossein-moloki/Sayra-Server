using System;

namespace Sayra.Backend.Contracts
{
    public class CreateOrganizationRequestDto
    {
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
    }

    public class OrganizationResponseDto
    {
        public Guid Id { get; set; }
        public string OrganizationId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class CreateSiteRequestDto
    {
        public Guid OrganizationId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Timezone { get; set; } = "UTC";
    }

    public class SiteResponseDto
    {
        public Guid Id { get; set; }
        public string SiteId { get; set; } = string.Empty;
        public Guid OrganizationId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Timezone { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class CreateZoneRequestDto
    {
        public Guid SiteId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
    }

    public class ZoneResponseDto
    {
        public Guid Id { get; set; }
        public string ZoneId { get; set; } = string.Empty;
        public Guid SiteId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class AssignWorkstationRequestDto
    {
        public Guid OrganizationId { get; set; }
        public Guid SiteId { get; set; }
        public Guid ZoneId { get; set; }
    }

    public class WorkstationAssignmentResponseDto
    {
        public Guid WorkstationId { get; set; }
        public string PcId { get; set; } = string.Empty;
        public Guid OrganizationId { get; set; }
        public Guid SiteId { get; set; }
        public Guid ZoneId { get; set; }
        public string SiteCode { get; set; } = string.Empty;
        public string ZoneCode { get; set; } = string.Empty;
        public DateTime AssignedAt { get; set; }
    }
}
