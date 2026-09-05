using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

#nullable enable

namespace Sayra.Backend.Application.Updates
{
    public interface IUpdateArtifactStorage
    {
        /// <summary>
        /// Streams content from input stream to temporary storage.
        /// Returns the temporary storage key.
        /// </summary>
        Task<string> SaveTemporaryArtifactAsync(Guid packageId, Stream contentStream, CancellationToken cancellationToken = default);

        /// <summary>
        /// Atomically moves/promotes a temporary artifact to its final storage key.
        /// </summary>
        Task FinalizeArtifactAsync(string tempStorageKey, string finalStorageKey, CancellationToken cancellationToken = default);

        /// <summary>
        /// Opens a readable stream to the specified artifact storage key.
        /// </summary>
        Task<Stream> OpenReadStreamAsync(string storageKey, CancellationToken cancellationToken = default);

        /// <summary>
        /// Determines whether an artifact exists at the given storage key.
        /// </summary>
        Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes the artifact at the given storage key if it exists.
        /// </summary>
        Task DeleteArtifactAsync(string storageKey, CancellationToken cancellationToken = default);

        /// <summary>
        /// Obtains physical/artifact byte size for the specified storage key.
        /// </summary>
        Task<long> GetArtifactSizeAsync(string storageKey, CancellationToken cancellationToken = default);
    }
}
