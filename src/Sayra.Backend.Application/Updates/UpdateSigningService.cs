using System;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Exceptions;

#nullable enable

namespace Sayra.Backend.Application.Updates
{
    public class UpdateSigningService : IUpdateSigningService
    {
        private readonly IUpdateSigningKeyProvider _keyProvider;
        private readonly ICryptographicService _cryptoService;

        public UpdateSigningService(
            IUpdateSigningKeyProvider keyProvider,
            ICryptographicService cryptoService)
        {
            _keyProvider = keyProvider ?? throw new ArgumentNullException(nameof(keyProvider));
            _cryptoService = cryptoService ?? throw new ArgumentNullException(nameof(cryptoService));
        }

        public async Task<UpdateSignatureResult> SignPackageAsync(
            UpdatePackage package,
            string? keyId = null,
            CancellationToken cancellationToken = default)
        {
            if (package == null)
            {
                throw new ArgumentNullException(nameof(package));
            }

            if (string.IsNullOrWhiteSpace(package.SHA256))
            {
                throw new InvalidDomainException("HASH_MISSING", $"Package '{package.FileName}' cannot be signed without an authoritative SHA-256 hash.");
            }

            ConfigurationSigningKey key;
            if (!string.IsNullOrWhiteSpace(keyId))
            {
                var foundKey = await _keyProvider.GetKeyAsync(keyId, cancellationToken);
                if (foundKey == null)
                {
                    throw new InvalidDomainException("KEY_NOT_FOUND", $"Signing key '{keyId}' was not found.");
                }
                key = foundKey;
            }
            else
            {
                key = await _keyProvider.GetActiveKeyAsync(cancellationToken);
            }

            if (key.Status != SigningKeyStatus.Active)
            {
                throw new InvalidDomainException("KEY_NOT_ACTIVE", $"Cannot sign update package using key '{key.KeyId}' with status '{key.Status}'. Active key required.");
            }

            if (!string.Equals(key.Algorithm, "RSA-SHA256", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDomainException("UNSUPPORTED_KEY_ALGORITHM", $"Unsupported signing key algorithm '{key.Algorithm}'. Expected 'RSA-SHA256'.");
            }

            byte[] canonicalBytes = UpdateSigningCanonicalizer.BuildCanonicalPayloadBytes(package);

            string privateKeyPem = await _keyProvider.GetPrivateKeyPemAsync(key.KeyId, cancellationToken);
            if (string.IsNullOrWhiteSpace(privateKeyPem))
            {
                throw new InvalidOperationException($"Private key for KeyId '{key.KeyId}' is unavailable.");
            }

            byte[] signatureBytes = _cryptoService.SignDataRsa(canonicalBytes, privateKeyPem);
            string signatureBase64 = Convert.ToBase64String(signatureBytes);

            return new UpdateSignatureResult
            {
                Hash = package.SHA256,
                Signature = signatureBase64,
                Algorithm = "RSA-SHA256",
                KeyId = key.KeyId
            };
        }

        public async Task<UpdateSignatureVerificationResult> VerifyPackageAsync(
            UpdatePackage package,
            CancellationToken cancellationToken = default)
        {
            if (package == null)
            {
                return UpdateSignatureVerificationResult.Failure("Update package is null.");
            }

            if (string.IsNullOrWhiteSpace(package.Signature))
            {
                return UpdateSignatureVerificationResult.Failure("Update package signature is missing.");
            }

            if (string.IsNullOrWhiteSpace(package.SigningKeyId))
            {
                return UpdateSignatureVerificationResult.Failure("Update package SigningKeyId is missing.");
            }

            return await VerifyPackageAsync(package, package.Signature, package.SigningKeyId, cancellationToken);
        }

        public async Task<UpdateSignatureVerificationResult> VerifyPackageAsync(
            UpdatePackage package,
            string signature,
            string keyId,
            CancellationToken cancellationToken = default)
        {
            if (package == null)
            {
                return UpdateSignatureVerificationResult.Failure("Update package is null.", keyId ?? string.Empty);
            }

            if (string.IsNullOrWhiteSpace(signature))
            {
                return UpdateSignatureVerificationResult.Failure("Signature is null or empty.", keyId ?? string.Empty);
            }

            if (string.IsNullOrWhiteSpace(keyId))
            {
                return UpdateSignatureVerificationResult.Failure("KeyId is null or empty.", keyId ?? string.Empty);
            }

            if (string.IsNullOrWhiteSpace(package.SHA256))
            {
                return UpdateSignatureVerificationResult.Failure("Package SHA-256 hash is null or empty.", keyId);
            }

            var key = await _keyProvider.GetKeyAsync(keyId, cancellationToken);
            if (key == null)
            {
                return UpdateSignatureVerificationResult.Failure($"Unknown KeyId '{keyId}'.", keyId);
            }

            if (key.Status == SigningKeyStatus.Revoked)
            {
                return UpdateSignatureVerificationResult.Failure($"Signing key '{keyId}' is revoked.", keyId);
            }

            if (!string.Equals(key.Algorithm, "RSA-SHA256", StringComparison.OrdinalIgnoreCase))
            {
                return UpdateSignatureVerificationResult.Failure($"Unsupported key algorithm '{key.Algorithm}'.", keyId);
            }

            byte[] canonicalBytes;
            try
            {
                canonicalBytes = UpdateSigningCanonicalizer.BuildCanonicalPayloadBytes(package);
            }
            catch (Exception ex)
            {
                return UpdateSignatureVerificationResult.Failure($"Failed to build canonical payload: {ex.Message}", keyId);
            }

            byte[] signatureBytes;
            try
            {
                signatureBytes = Convert.FromBase64String(signature);
            }
            catch
            {
                return UpdateSignatureVerificationResult.Failure("Malformed Base64 signature string.", keyId);
            }

            string publicKeyPem = await _keyProvider.GetPublicKeyPemAsync(key.KeyId, cancellationToken);
            if (string.IsNullOrWhiteSpace(publicKeyPem))
            {
                publicKeyPem = key.PublicKeyPem;
            }

            bool isSignatureValid = _cryptoService.VerifyDataRsa(canonicalBytes, signatureBytes, publicKeyPem);
            if (!isSignatureValid)
            {
                return UpdateSignatureVerificationResult.Failure("Digital signature verification failed.", keyId);
            }

            return UpdateSignatureVerificationResult.Success(keyId);
        }
    }
}
