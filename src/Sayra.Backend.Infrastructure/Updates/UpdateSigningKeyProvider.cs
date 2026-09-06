using System;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Updates;
using Sayra.Backend.Domain;
using Sayra.Backend.Infrastructure.Security;

#nullable enable

namespace Sayra.Backend.Infrastructure.Updates
{
    public class UpdateSigningKeyProvider : IUpdateSigningKeyProvider
    {
        private readonly IConfigurationKeyRegistryRepository _keyRegistryRepository;
        private readonly ISigningPrivateKeyProvider _privateKeyProvider;

        public UpdateSigningKeyProvider(
            IConfigurationKeyRegistryRepository keyRegistryRepository,
            ISigningPrivateKeyProvider privateKeyProvider)
        {
            _keyRegistryRepository = keyRegistryRepository ?? throw new ArgumentNullException(nameof(keyRegistryRepository));
            _privateKeyProvider = privateKeyProvider ?? throw new ArgumentNullException(nameof(privateKeyProvider));
        }

        public async Task<ConfigurationSigningKey?> GetKeyAsync(string keyId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(keyId))
            {
                throw new ArgumentException("KeyId cannot be null or empty.", nameof(keyId));
            }

            return await _keyRegistryRepository.GetByKeyIdAsync(keyId.Trim(), cancellationToken);
        }

        public async Task<ConfigurationSigningKey> GetActiveKeyAsync(CancellationToken cancellationToken = default)
        {
            var key = await _keyRegistryRepository.GetActiveKeyAsync(cancellationToken);
            if (key != null)
            {
                return key;
            }

            // Fall back to creating or retrieving an active signing key in key registry if none exists
            var (ephemeralKeyId, ephemeralPublicPem, _) = SigningPrivateKeyProvider.GetOrCreateEphemeralKeyPair("update-signing-active-01");
            var newKey = ConfigurationSigningKey.Create(ephemeralKeyId, ephemeralPublicPem, "RSA-SHA256", SigningKeyStatus.Active);
            await _keyRegistryRepository.AddAsync(newKey, cancellationToken);
            return newKey;
        }

        public async Task<string> GetPublicKeyPemAsync(string keyId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(keyId))
            {
                throw new ArgumentException("KeyId cannot be null or empty.", nameof(keyId));
            }

            return await _privateKeyProvider.GetPublicKeyPemAsync(keyId.Trim(), cancellationToken);
        }

        public async Task<string> GetPrivateKeyPemAsync(string keyId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(keyId))
            {
                throw new ArgumentException("KeyId cannot be null or empty.", nameof(keyId));
            }

            return await _privateKeyProvider.GetPrivateKeyPemAsync(keyId.Trim(), cancellationToken);
        }
    }
}
