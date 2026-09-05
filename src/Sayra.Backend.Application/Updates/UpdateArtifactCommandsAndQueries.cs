using System;
using System.IO;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;

#nullable enable

namespace Sayra.Backend.Application.Updates
{
    public class UploadUpdatePackageCommand : ICommand<ClientUpdatePackageMetadataContract>
    {
        public Guid ReleaseId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public Stream ContentStream { get; set; } = Stream.Null;
        public string? DeclaredSha256 { get; set; }
        public UpdatePackageType PackageType { get; set; } = UpdatePackageType.Spk;
        public UserPrincipal Principal { get; set; } = UserPrincipal.Anonymous;
    }

    public class ValidateUpdatePackageCommand : ICommand<ClientUpdatePackageMetadataContract>
    {
        public Guid PackageId { get; set; }
        public UserPrincipal Principal { get; set; } = UserPrincipal.Anonymous;
    }

    public class GetUpdatePackageQuery : IQuery<ClientUpdatePackageMetadataContract>
    {
        public Guid PackageId { get; set; }
        public UserPrincipal Principal { get; set; } = UserPrincipal.Anonymous;
    }

    public class DeleteUpdatePackageCommand : ICommand<bool>
    {
        public Guid PackageId { get; set; }
        public UserPrincipal Principal { get; set; } = UserPrincipal.Anonymous;
    }
}
