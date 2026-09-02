using System;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Domain;

#nullable enable

namespace Sayra.Backend.Application.Configuration
{
    public class ConfigurationSigningService : IConfigurationSigningService
    {
        private readonly ICanonicalConfigurationSerializer _canonicalSerializer;
        private readonly IConfigurationHashService _hashService;
        private readonly IConfigurationKeyRegistry _keyRegistry;
        private readonly ISigningPrivateKeyProvider _privateKeyProvider;
        private readonly ICryptographicService _cryptoService;

        public ConfigurationSigningService(
            ICanonicalConfigurationSerializer canonicalSerializer,
            IConfigurationHashService hashService,
            IConfigurationKeyRegistry keyRegistry,
            ISigningPrivateKeyProvider privateKeyProvider,
            ICryptographicService cryptoService)
        {
            _canonicalSerializer = canonicalSerializer ?? throw new ArgumentNullException(nameof(canonicalSerializer));
            _hashService = hashService ?? throw new ArgumentNullException(nameof(hashService));
            _keyRegistry = keyRegistry ?? throw new ArgumentNullException(nameof(keyRegistry));
            _privateKeyProvider = privateKeyProvider ?? throw new ArgumentNullException(nameof(privateKeyProvider));
            _cryptoService = cryptoService ?? throw new ArgumentNullException(nameof(cryptoService));
        }

        public async Task<ConfigurationSignatureResult> SignPackageAsync(string content, string? keyId = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new ArgumentException("Configuration content cannot be null or empty.", nameof(content));
            }

            ConfigurationSigningKey key;
            if (!string.IsNullOrWhiteSpace(keyId))
            {
                var foundKey = await _keyRegistry.GetKeyAsync(keyId, cancellationToken);
                if (foundKey == null)
                {
                    throw new InvalidOperationException($"Signing key '{keyId}' was not found in key registry.");
                }
                key = foundKey;
            }
            else
            {
                key = await _keyRegistry.GetActiveKeyAsync(cancellationToken);
            }

            if (key.Status != SigningKeyStatus.Active)
            {
                throw new InvalidOperationException($"Cannot sign configuration using key '{key.KeyId}' with status '{key.Status}'. Active key required.");
            }

            if (!string.Equals(key.Algorithm, "RSA-SHA256", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Unsupported signing key algorithm '{key.Algorithm}'. Expected 'RSA-SHA256'.");
            }

            byte[] canonicalBytes = _canonicalSerializer.SerializeToCanonicalBytes(content);
            string hashHex = _hashService.ComputeHash(canonicalBytes);

            string privateKeyPem = await _privateKeyProvider.GetPrivateKeyPemAsync(key.KeyId, cancellationToken);
            if (string.IsNullOrWhiteSpace(privateKeyPem))
            {
                throw new InvalidOperationException($"Private key for KeyId '{key.KeyId}' is unavailable.");
            }

            byte[] signatureBytes = _cryptoService.SignDataRsa(canonicalBytes, privateKeyPem);
            string signatureBase64 = Convert.ToBase64String(signatureBytes);

            return new ConfigurationSignatureResult
            {
                Hash = hashHex,
                Signature = signatureBase64,
                Algorithm = "RSA-SHA256",
                KeyId = key.KeyId
            };
        }

        public async Task<ConfigurationVerificationResult> VerifyPackageAsync(
            string content,
            string hash,
            string signature,
            string keyId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return ConfigurationVerificationResult.Failure("Configuration content is null or empty.", keyId);
            }

            if (string.IsNullOrWhiteSpace(hash))
            {
                return ConfigurationVerificationResult.Failure("Configuration hash is null or empty.", keyId);
            }

            if (string.IsNullOrWhiteSpace(signature))
            {
                return ConfigurationVerificationResult.Failure("Signature is null or empty.", keyId);
            }

            if (string.IsNullOrWhiteSpace(keyId))
            {
                return ConfigurationVerificationResult.Failure("KeyId is null or empty.", keyId);
            }

            var key = await _keyRegistry.GetKeyAsync(keyId, cancellationToken);
            if (key == null)
            {
                return ConfigurationVerificationResult.Failure($"Unknown KeyId '{keyId}'.", keyId);
            }

            if (key.Status == SigningKeyStatus.Revoked)
            {
                return ConfigurationVerificationResult.Failure($"Signing key '{keyId}' is revoked.", keyId);
            }

            if (!string.Equals(key.Algorithm, "RSA-SHA256", StringComparison.OrdinalIgnoreCase))
            {
                return ConfigurationVerificationResult.Failure($"Unsupported key algorithm '{key.Algorithm}'.", keyId);
            }

            byte[] canonicalBytes = _canonicalSerializer.SerializeToCanonicalBytes(content);
            if (!_hashService.VerifyHash(canonicalBytes, hash))
            {
                return ConfigurationVerificationResult.Failure("Computed payload hash does not match expected hash.", keyId);
            }

            byte[] signatureBytes;
            try
            {
                signatureBytes = Convert.FromBase64String(signature);
            }
            catch
            {
                return ConfigurationVerificationResult.Failure("Malformed Base64 signature string.", keyId);
            }

            bool isSignatureValid = _cryptoService.VerifyDataRsa(canonicalBytes, signatureBytes, key.PublicKeyPem);
            if (!isSignatureValid)
            {
                return ConfigurationVerificationResult.Failure("Digital signature verification failed.", keyId);
            }

            return ConfigurationVerificationResult.Success(keyId);
        }
    }
}
