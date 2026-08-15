using System;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Application.Workstations
{
    public class AssignWorkstationCommand : ICommand<WorkstationAssignmentResponseDto>
    {
        public Guid WorkstationId { get; set; }
        public Guid OrganizationId { get; set; }
        public Guid SiteId { get; set; }
        public Guid ZoneId { get; set; }
    }

    public class GetWorkstationAssignmentQuery : IQuery<WorkstationAssignmentResponseDto>
    {
        public Guid WorkstationId { get; set; }
    }
}
