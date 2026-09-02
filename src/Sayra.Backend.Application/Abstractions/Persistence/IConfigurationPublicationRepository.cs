using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Application.Abstractions.Persistence
{
    public interface IConfigurationPublicationRepository
    {
        Task<ConfigurationPublication?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<ConfigurationPublication>> GetPublicationsByPackageIdAsync(Guid packageId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<ConfigurationPublication>> GetPublicationsByTargetIdAsync(Guid targetId, CancellationToken cancellationToken = default);
        Task AddAsync(ConfigurationPublication publication, CancellationToken cancellationToken = default);
        Task UpdateAsync(ConfigurationPublication publication, CancellationToken cancellationToken = default);
    }
}
