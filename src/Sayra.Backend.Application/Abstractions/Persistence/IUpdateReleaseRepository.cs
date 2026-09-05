using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Application.Abstractions.Persistence
{
    public interface IUpdateReleaseRepository : IRepository<UpdateRelease>
    {
        Task<UpdateRelease?> GetByOrganizationAndVersionAsync(Guid organizationId, string version, bool track = true, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<UpdateRelease>> GetByOrganizationIdAsync(Guid organizationId, bool track = true, CancellationToken cancellationToken = default);
        Task<UpdateRelease?> GetActiveReleaseAsync(Guid organizationId, bool track = true, CancellationToken cancellationToken = default);
    }
}
