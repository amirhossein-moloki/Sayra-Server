using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Domain;

#nullable enable

namespace Sayra.Backend.Application.Configuration
{
    public class ConfigurationKeyRegistry : IConfigurationKeyRegistry
    {
        private readonly IConfigurationKeyRegistryRepository _repository;
        private readonly ISigningPrivateKeyProvider _privateKeyProvider;
        private readonly IUnitOfWork _unitOfWork;

        public ConfigurationKeyRegistry(
            IConfigurationKeyRegistryRepository repository,
            ISigningPrivateKeyProvider privateKeyProvider,
            IUnitOfWork unitOfWork)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _privateKeyProvider = privateKeyProvider ?? throw new ArgumentNullException(nameof(privateKeyProvider));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        }

        public async Task<ConfigurationSigningKey?> GetKeyAsync(string keyId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(keyId))
            {
                return null;
            }

            return await _repository.GetByKeyIdAsync(keyId.Trim(), cancellationToken);
        }

        public async Task<ConfigurationSigningKey> GetActiveKeyAsync(CancellationToken cancellationToken = default)
        {
            var active = await _repository.GetActiveKeyAsync(cancellationToken);
            if (active != null)
            {
                return active;
            }

            // Fallback / Auto-provision initial active key in registry if empty
            string activeKeyId = "config-signing-active-01";
            string activePublicKeyPem = await _privateKeyProvider.GetActivePublicKeyPemAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(activePublicKeyPem))
            {
                throw new InvalidOperationException("No active configuration signing key registered and unable to auto-provision default key.");
            }

            var newKey = ConfigurationSigningKey.Create(
                keyId: activeKeyId,
                publicKeyPem: activePublicKeyPem,
                algorithm: "RSA-SHA256",
                status: SigningKeyStatus.Active);

            await _repository.AddAsync(newKey, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return newKey;
        }

        public async Task<List<ConfigurationSigningKey>> GetAllKeysAsync(CancellationToken cancellationToken = default)
        {
            return await _repository.GetAllKeysAsync(cancellationToken);
        }

        public async Task<ConfigurationSigningKey> RegisterKeyAsync(
            string keyId,
            string publicKeyPem,
            string algorithm = "RSA-SHA256",
            SigningKeyStatus status = SigningKeyStatus.Active,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(keyId)) throw new ArgumentException("KeyId cannot be null or empty.", nameof(keyId));
            if (string.IsNullOrWhiteSpace(publicKeyPem)) throw new ArgumentException("PublicKeyPem cannot be null or empty.", nameof(publicKeyPem));

            var existing = await _repository.GetByKeyIdAsync(keyId.Trim(), cancellationToken);
            if (existing != null)
            {
                throw new InvalidOperationException($"Signing key with KeyId '{keyId}' already exists.");
            }

            var key = ConfigurationSigningKey.Create(
                keyId: keyId,
                publicKeyPem: publicKeyPem,
                algorithm: algorithm,
                status: status);

            await _repository.AddAsync(key, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return key;
        }

        public async Task<bool> RotateActiveKeyAsync(string newKeyId, string newPublicKeyPem, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(newKeyId)) throw new ArgumentException("New KeyId cannot be null or empty.", nameof(newKeyId));
            if (string.IsNullOrWhiteSpace(newPublicKeyPem)) throw new ArgumentException("New PublicKeyPem cannot be null or empty.", nameof(newPublicKeyPem));

            return await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                var currentActive = await _repository.GetActiveKeyAsync(cancellationToken);
                if (currentActive != null)
                {
                    currentActive.Retire();
                    _repository.Update(currentActive);
                }

                var newActiveKey = ConfigurationSigningKey.Create(
                    keyId: newKeyId,
                    publicKeyPem: newPublicKeyPem,
                    algorithm: "RSA-SHA256",
                    status: SigningKeyStatus.Active);

                await _repository.AddAsync(newActiveKey, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return true;
            }, cancellationToken);
        }

        public async Task<bool> RevokeKeyAsync(string keyId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(keyId)) return false;

            return await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                var key = await _repository.GetByKeyIdAsync(keyId.Trim(), cancellationToken);
                if (key == null)
                {
                    return false;
                }

                key.Revoke();
                _repository.Update(key);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return true;
            }, cancellationToken);
        }
    }
}
