using System;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Application.Organizations
{
    public class CreateOrganizationCommand : ICommand<Organization>
    {
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
    }

    public class DeactivateOrganizationCommand : ICommand<Organization>
    {
        public Guid OrganizationId { get; set; }
    }

    public class GetOrganizationQuery : IQuery<Organization>
    {
        public Guid OrganizationId { get; set; }
    }
}
