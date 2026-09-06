using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Domain;

#nullable enable

namespace Sayra.Backend.Application.Updates
{
    public interface IUpdateSigningKeyProvider
    {
        Task<ConfigurationSigningKey?> GetKeyAsync(string keyId, CancellationToken cancellationToken = default);
        Task<ConfigurationSigningKey> GetActiveKeyAsync(CancellationToken cancellationToken = default);
        Task<string> GetPublicKeyPemAsync(string keyId, CancellationToken cancellationToken = default);
        Task<string> GetPrivateKeyPemAsync(string keyId, CancellationToken cancellationToken = default);
    }
}
