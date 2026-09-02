using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Application.Abstractions.Persistence
{
    public interface IConfigurationPublicationRepository : IRepository<ConfigurationPublication>
    {
        Task<ConfigurationPublication?> GetActivePublicationForTargetAsync(Guid targetId, CancellationToken cancellationToken = default);
        Task<ConfigurationPublication?> GetByPackageAndTargetAsync(Guid packageId, Guid targetId, CancellationToken cancellationToken = default);
        Task<ConfigurationPublication?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default);
        Task<List<ConfigurationPublication>> GetPublicationsForTargetAsync(Guid targetId, CancellationToken cancellationToken = default);
        Task<List<ConfigurationPublication>> GetPublicationsForPackageAsync(Guid packageId, CancellationToken cancellationToken = default);
    }
}
