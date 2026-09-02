using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Domain;

#nullable enable

namespace Sayra.Backend.Application.Configuration
{
    public interface IConfigurationKeyRegistry
    {
        Task<ConfigurationSigningKey?> GetKeyAsync(string keyId, CancellationToken cancellationToken = default);
        Task<ConfigurationSigningKey> GetActiveKeyAsync(CancellationToken cancellationToken = default);
        Task<List<ConfigurationSigningKey>> GetAllKeysAsync(CancellationToken cancellationToken = default);
        Task<ConfigurationSigningKey> RegisterKeyAsync(string keyId, string publicKeyPem, string algorithm = "RSA-SHA256", SigningKeyStatus status = SigningKeyStatus.Active, CancellationToken cancellationToken = default);
        Task<bool> RotateActiveKeyAsync(string newKeyId, string newPublicKeyPem, CancellationToken cancellationToken = default);
        Task<bool> RevokeKeyAsync(string keyId, CancellationToken cancellationToken = default);
    }
}
