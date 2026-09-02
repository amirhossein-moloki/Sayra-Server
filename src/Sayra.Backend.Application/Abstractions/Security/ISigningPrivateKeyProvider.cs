using System.Threading;
using System.Threading.Tasks;

#nullable enable

namespace Sayra.Backend.Application.Abstractions.Security
{
    public interface ISigningPrivateKeyProvider
    {
        Task<string> GetPrivateKeyPemAsync(string keyId, CancellationToken cancellationToken = default);
        Task<string> GetActivePrivateKeyPemAsync(CancellationToken cancellationToken = default);
        Task<string> GetPublicKeyPemAsync(string keyId, CancellationToken cancellationToken = default);
        Task<string> GetActivePublicKeyPemAsync(CancellationToken cancellationToken = default);
    }
}
