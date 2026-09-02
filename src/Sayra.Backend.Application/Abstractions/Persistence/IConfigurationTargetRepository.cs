using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Application.Abstractions.Persistence
{
    public interface IConfigurationTargetRepository
    {
        Task<ConfigurationTarget?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
        Task<ConfigurationTarget?> GetByTypeAndIdentifierAsync(ConfigurationTargetType targetType, string identifier, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<ConfigurationTarget>> GetByTargetTypeAsync(ConfigurationTargetType targetType, CancellationToken cancellationToken = default);
        Task AddAsync(ConfigurationTarget target, CancellationToken cancellationToken = default);
        Task UpdateAsync(ConfigurationTarget target, CancellationToken cancellationToken = default);
    }
}
