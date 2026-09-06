using System;
using System.Collections.Generic;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;

#nullable enable

namespace Sayra.Backend.Application.Updates
{
    // --- DTOs ---

    public class UpdateTargetContract
    {
        public Guid TargetId { get; set; }
        public Guid OrganizationId { get; set; }
        public Guid ReleaseId { get; set; }
        public string TargetType { get; set; } = string.Empty;
        public Guid? SiteId { get; set; }
        public Guid? GroupId { get; set; }
        public Guid? WorkstationId { get; set; }
        public int RolloutPercentage { get; set; } = 100;
        public bool IsEnabled { get; set; } = true;
        public string? MinimumSupportedVersion { get; set; }
        public bool IsMandatoryOverride { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public ClientUpdateReleaseContract? Release { get; set; }
    }

    public class CreateUpdateTargetRequest
    {
        public Guid ReleaseId { get; set; }
        public string TargetType { get; set; } = "Global";
        public Guid? SiteId { get; set; }
        public Guid? GroupId { get; set; }
        public Guid? WorkstationId { get; set; }
        public int RolloutPercentage { get; set; } = 100;
        public string? MinimumSupportedVersion { get; set; }
        public bool IsMandatoryOverride { get; set; }
    }

    public class UpdateRolloutPercentageRequest
    {
        public int RolloutPercentage { get; set; }
    }

    public class EvaluateEligibilityRequest
    {
        public Guid WorkstationId { get; set; }
        public string? ReportedVersion { get; set; }
        public string? OsVersion { get; set; }
        public string? Architecture { get; set; }
    }

    // --- Commands ---

    public class CreateUpdateTargetCommand : ICommand<UpdateTargetContract>
    {
        public Guid OrganizationId { get; set; }
        public Guid ReleaseId { get; set; }
        public ConfigurationTargetType TargetType { get; set; }
        public Guid? SiteId { get; set; }
        public Guid? GroupId { get; set; }
        public Guid? WorkstationId { get; set; }
        public int RolloutPercentage { get; set; } = 100;
        public string? MinimumSupportedVersion { get; set; }
        public bool IsMandatoryOverride { get; set; }
        public UserPrincipal Principal { get; set; } = UserPrincipal.Anonymous;
    }

    public class UpdateRolloutPercentageCommand : ICommand<UpdateTargetContract>
    {
        public Guid TargetId { get; set; }
        public int RolloutPercentage { get; set; }
        public UserPrincipal Principal { get; set; } = UserPrincipal.Anonymous;
    }

    public class DisableUpdateTargetCommand : ICommand<UpdateTargetContract>
    {
        public Guid TargetId { get; set; }
        public UserPrincipal Principal { get; set; } = UserPrincipal.Anonymous;
    }

    public class DeleteUpdateTargetCommand : ICommand<bool>
    {
        public Guid TargetId { get; set; }
        public UserPrincipal Principal { get; set; } = UserPrincipal.Anonymous;
    }

    // --- Queries ---

    public class GetUpdateTargetsByReleaseQuery : IQuery<IReadOnlyList<UpdateTargetContract>>
    {
        public Guid ReleaseId { get; set; }
        public UserPrincipal Principal { get; set; } = UserPrincipal.Anonymous;
    }

    public class GetUpdateTargetsByOrganizationQuery : IQuery<IReadOnlyList<UpdateTargetContract>>
    {
        public Guid OrganizationId { get; set; }
        public UserPrincipal Principal { get; set; } = UserPrincipal.Anonymous;
    }

    public class EvaluateWorkstationUpdateEligibilityQuery : IQuery<UpdateEligibilityResult>
    {
        public Guid WorkstationId { get; set; }
        public string? ReportedVersion { get; set; }
        public string? OsVersion { get; set; }
        public string? Architecture { get; set; }
        public UserPrincipal Principal { get; set; } = UserPrincipal.Anonymous;
    }
}
