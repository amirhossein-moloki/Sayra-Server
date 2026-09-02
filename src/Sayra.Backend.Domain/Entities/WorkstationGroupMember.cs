using System;
using Sayra.Backend.Domain.Exceptions;

namespace Sayra.Backend.Domain
{
    public class WorkstationGroupMember
    {
        public Guid WorkstationGroupId { get; set; }
        public Guid WorkstationId { get; set; }
        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

        public void Validate()
        {
            if (WorkstationGroupId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_GROUP_ID", "WorkstationGroupId is required.");
            }

            if (WorkstationId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_WORKSTATION_ID", "WorkstationId is required.");
            }
        }
    }
}
