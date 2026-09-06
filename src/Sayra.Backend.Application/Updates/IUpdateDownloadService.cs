using System.Threading;
using System.Threading.Tasks;

#nullable enable

namespace Sayra.Backend.Application.Updates
{
    public interface IUpdateDownloadService
    {
        /// <summary>
        /// Authorizes, validates, and prepares an update package streaming response for an authenticated workstation.
        /// </summary>
        Task<UpdateDownloadPreparation> PrepareDownloadAsync(
            UpdateDownloadRequest request,
            CancellationToken cancellationToken = default);
    }
}
