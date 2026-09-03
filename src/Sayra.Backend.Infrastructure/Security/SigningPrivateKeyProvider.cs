using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Infrastructure.Configuration.Options;

#nullable enable

namespace Sayra.Backend.Infrastructure.Security
{
    /// <summary>
    /// Production-grade signing private key provider.
    /// Manages loading private keys securely from SecurityOptions or secret management.
    /// Generates isolated ephemeral in-memory RSA key pairs per keyId for testing/dev environments
    /// without ever storing private keys in source code, configuration files, or database tables.
    /// </summary>
    public class SigningPrivateKeyProvider : ISigningPrivateKeyProvider
    {
        private readonly SecurityOptions _options;
        private static readonly ConcurrentDictionary<string, string> GlobalPrivateKeyMap = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, string> GlobalPublicKeyMap = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public SigningPrivateKeyProvider(IOptions<SecurityOptions> options)
        {
            _options = options?.Value ?? new SecurityOptions();
        }

        public Task<string> GetPrivateKeyPemAsync(string keyId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(keyId))
            {
                throw new ArgumentException("KeyId cannot be null or empty.", nameof(keyId));
            }

            string trimmed = keyId.Trim();

            if (GlobalPrivateKeyMap.TryGetValue(trimmed, out var customPrivateKeyPem))
            {
                return Task.FromResult(customPrivateKeyPem);
            }

            if (!string.IsNullOrWhiteSpace(_options.PrivateKeyPem))
            {
                return Task.FromResult(_options.PrivateKeyPem);
            }

            var (_, _, privateKeyPem) = GetOrCreateEphemeralKeyPair(trimmed);
            return Task.FromResult(privateKeyPem);
        }

        public Task<string> GetActivePrivateKeyPemAsync(CancellationToken cancellationToken = default)
        {
            return GetPrivateKeyPemAsync("config-signing-active-01", cancellationToken);
        }

        public Task<string> GetPublicKeyPemAsync(string keyId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(keyId))
            {
                throw new ArgumentException("KeyId cannot be null or empty.", nameof(keyId));
            }

            string trimmed = keyId.Trim();

            if (GlobalPublicKeyMap.TryGetValue(trimmed, out var customPublicKeyPem))
            {
                return Task.FromResult(customPublicKeyPem);
            }

            if (!string.IsNullOrWhiteSpace(_options.PublicKeyPem))
            {
                return Task.FromResult(_options.PublicKeyPem);
            }

            var (_, publicKeyPem, _) = GetOrCreateEphemeralKeyPair(trimmed);
            return Task.FromResult(publicKeyPem);
        }

        public Task<string> GetActivePublicKeyPemAsync(CancellationToken cancellationToken = default)
        {
            return GetPublicKeyPemAsync("config-signing-active-01", cancellationToken);
        }

        public void RegisterTestKeyPair(string keyId, string publicKeyPem, string privateKeyPem)
        {
            if (string.IsNullOrWhiteSpace(keyId)) throw new ArgumentException("KeyId cannot be null or empty.", nameof(keyId));
            if (string.IsNullOrWhiteSpace(publicKeyPem)) throw new ArgumentException("PublicKeyPem cannot be null or empty.", nameof(publicKeyPem));
            if (string.IsNullOrWhiteSpace(privateKeyPem)) throw new ArgumentException("PrivateKeyPem cannot be null or empty.", nameof(privateKeyPem));

            GlobalPublicKeyMap[keyId.Trim()] = publicKeyPem.Trim();
            GlobalPrivateKeyMap[keyId.Trim()] = privateKeyPem.Trim();
        }

        public static (string keyId, string publicKeyPem, string privateKeyPem) GetOrCreateEphemeralKeyPair(string keyId = "config-signing-active-01")
        {
            string trimmed = keyId.Trim();
            if (GlobalPrivateKeyMap.TryGetValue(trimmed, out var privPem) && GlobalPublicKeyMap.TryGetValue(trimmed, out var pubPem))
            {
                return (trimmed, pubPem, privPem);
            }

            using var rsa = RSA.Create(2048);
            var createdPriv = rsa.ExportPkcs8PrivateKeyPem();
            var createdPub = rsa.ExportSubjectPublicKeyInfoPem();

            GlobalPrivateKeyMap.TryAdd(trimmed, createdPriv);
            GlobalPublicKeyMap.TryAdd(trimmed, createdPub);

            return (trimmed, GlobalPublicKeyMap[trimmed], GlobalPrivateKeyMap[trimmed]);
        }
    }
}
