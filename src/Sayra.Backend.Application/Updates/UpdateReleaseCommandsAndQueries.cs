using System;
using System.Collections.Generic;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;

#nullable enable

namespace Sayra.Backend.Application.Updates
{
    public class CreateUpdateReleaseCommand : ICommand<ClientUpdateReleaseContract>
    {
        public Guid OrganizationId { get; set; }
        public string Version { get; set; } = string.Empty;
        public UpdateReleaseType ReleaseType { get; set; } = UpdateReleaseType.Standard;
        public string? ReleaseNotes { get; set; }
        public string? Metadata { get; set; }
        public UserPrincipal Principal { get; set; } = UserPrincipal.Anonymous;
    }

    public class UpdateReleaseMetadataCommand : ICommand<ClientUpdateReleaseContract>
    {
        public Guid ReleaseId { get; set; }
        public string? ReleaseNotes { get; set; }
        public string? Metadata { get; set; }
        public UserPrincipal Principal { get; set; } = UserPrincipal.Anonymous;
    }

    public class PrepareUpdateReleaseCommand : ICommand<ClientUpdateReleaseContract>
    {
        public Guid ReleaseId { get; set; }
        public UserPrincipal Principal { get; set; } = UserPrincipal.Anonymous;
    }

    public class PublishUpdateReleaseCommand : ICommand<ClientUpdateReleaseContract>
    {
        public Guid ReleaseId { get; set; }
        public UserPrincipal Principal { get; set; } = UserPrincipal.Anonymous;
    }

    public class ActivateUpdateReleaseCommand : ICommand<ClientUpdateReleaseContract>
    {
        public Guid ReleaseId { get; set; }
        public UserPrincipal Principal { get; set; } = UserPrincipal.Anonymous;
    }

    public class RevokeUpdateReleaseCommand : ICommand<ClientUpdateReleaseContract>
    {
        public Guid ReleaseId { get; set; }
        public string Reason { get; set; } = string.Empty;
        public UserPrincipal Principal { get; set; } = UserPrincipal.Anonymous;
    }

    public class RollbackUpdateReleaseCommand : ICommand<ClientUpdateReleaseContract>
    {
        public Guid CurrentReleaseId { get; set; }
        public Guid TargetReleaseId { get; set; }
        public string Reason { get; set; } = string.Empty;
        public UserPrincipal Principal { get; set; } = UserPrincipal.Anonymous;
    }

    public class GetUpdateReleaseByIdQuery : IQuery<ClientUpdateReleaseContract>
    {
        public Guid ReleaseId { get; set; }
        public UserPrincipal Principal { get; set; } = UserPrincipal.Anonymous;
    }

    public class GetUpdateReleasesByOrganizationQuery : IQuery<IReadOnlyList<ClientUpdateReleaseContract>>
    {
        public Guid OrganizationId { get; set; }
        public UserPrincipal Principal { get; set; } = UserPrincipal.Anonymous;
    }

    public class GetActiveUpdateReleaseQuery : IQuery<ClientUpdateReleaseContract>
    {
        public Guid OrganizationId { get; set; }
        public UserPrincipal Principal { get; set; } = UserPrincipal.Anonymous;
    }
}
