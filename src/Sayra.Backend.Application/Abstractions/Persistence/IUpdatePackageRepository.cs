using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Application.Abstractions.Persistence
{
    public interface IUpdatePackageRepository : IRepository<UpdatePackage>
    {
        Task<IReadOnlyList<UpdatePackage>> GetByReleaseIdAsync(Guid releaseId, bool track = true, CancellationToken cancellationToken = default);
        Task<UpdatePackage?> GetByStorageKeyAsync(string storageKey, bool track = true, CancellationToken cancellationToken = default);
        Task<UpdatePackage?> GetBySha256Async(string sha256, bool track = true, CancellationToken cancellationToken = default);
    }
}
