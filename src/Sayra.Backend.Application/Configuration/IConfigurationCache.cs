using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Configuration.Models;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Application.Configuration
{
    public interface IConfigurationCache
    {
        Task<CachedEffectiveConfiguration?> GetEffectiveConfigurationAsync(
            Guid organizationId,
            Guid workstationId,
            Guid? siteId,
            List<Guid> groupIds,
            CancellationToken cancellationToken = default);

        Task SetEffectiveConfigurationAsync(
            Guid organizationId,
            Guid workstationId,
            Guid? siteId,
            List<Guid> groupIds,
            CachedEffectiveConfiguration cachedConfig,
            CancellationToken cancellationToken = default);

        Task InvalidateWorkstationAsync(
            Guid organizationId,
            Guid workstationId,
            CancellationToken cancellationToken = default);

        Task InvalidateScopeAsync(
            Guid organizationId,
            ConfigurationTargetType targetType,
            Guid? targetId,
            CancellationToken cancellationToken = default);

        Task InvalidateTargetAsync(
            Guid organizationId,
            Guid targetId,
            CancellationToken cancellationToken = default);

        Task<CachedPublicationMetadata?> GetPublicationMetadataAsync(
            Guid organizationId,
            Guid targetId,
            CancellationToken cancellationToken = default);

        Task SetPublicationMetadataAsync(
            Guid organizationId,
            Guid targetId,
            CachedPublicationMetadata metadata,
            CancellationToken cancellationToken = default);

        Task InvalidatePublicationMetadataAsync(
            Guid organizationId,
            Guid targetId,
            CancellationToken cancellationToken = default);

        Task<IDisposable?> AcquireStampedeLockAsync(
            Guid organizationId,
            Guid workstationId,
            CancellationToken cancellationToken = default);
    }
}
