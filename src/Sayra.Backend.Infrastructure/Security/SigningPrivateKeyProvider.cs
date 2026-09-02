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
    /// Generates isolated ephemeral in-memory RSA key pairs for testing/dev environments
    /// without ever storing private keys in source code, configuration files, or database tables.
    /// </summary>
    public class SigningPrivateKeyProvider : ISigningPrivateKeyProvider
    {
        private readonly SecurityOptions _options;
        private readonly ConcurrentDictionary<string, string> _privateKeyMap = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, string> _publicKeyMap = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object SyncLock = new object();
        private static string? EphemeralActiveKeyId;
        private static string? EphemeralActivePrivateKeyPem;
        private static string? EphemeralActivePublicKeyPem;

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

            if (_privateKeyMap.TryGetValue(keyId.Trim(), out var customPrivateKeyPem))
            {
                return Task.FromResult(customPrivateKeyPem);
            }

            if (!string.IsNullOrWhiteSpace(_options.PrivateKeyPem))
            {
                return Task.FromResult(_options.PrivateKeyPem);
            }

            EnsureEphemeralKeyPair();
            if (string.Equals(keyId.Trim(), EphemeralActiveKeyId, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(EphemeralActivePrivateKeyPem!);
            }

            throw new InvalidOperationException($"Private key for KeyId '{keyId}' was not found in secret store.");
        }

        public Task<string> GetActivePrivateKeyPemAsync(CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrWhiteSpace(_options.PrivateKeyPem))
            {
                return Task.FromResult(_options.PrivateKeyPem);
            }

            EnsureEphemeralKeyPair();
            return Task.FromResult(EphemeralActivePrivateKeyPem!);
        }

        public Task<string> GetPublicKeyPemAsync(string keyId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(keyId))
            {
                throw new ArgumentException("KeyId cannot be null or empty.", nameof(keyId));
            }

            if (_publicKeyMap.TryGetValue(keyId.Trim(), out var customPublicKeyPem))
            {
                return Task.FromResult(customPublicKeyPem);
            }

            if (!string.IsNullOrWhiteSpace(_options.PublicKeyPem))
            {
                return Task.FromResult(_options.PublicKeyPem);
            }

            EnsureEphemeralKeyPair();
            return Task.FromResult(EphemeralActivePublicKeyPem!);
        }

        public Task<string> GetActivePublicKeyPemAsync(CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrWhiteSpace(_options.PublicKeyPem))
            {
                return Task.FromResult(_options.PublicKeyPem);
            }

            EnsureEphemeralKeyPair();
            return Task.FromResult(EphemeralActivePublicKeyPem!);
        }

        public void RegisterTestKeyPair(string keyId, string publicKeyPem, string privateKeyPem)
        {
            if (string.IsNullOrWhiteSpace(keyId)) throw new ArgumentException("KeyId cannot be null or empty.", nameof(keyId));
            if (string.IsNullOrWhiteSpace(publicKeyPem)) throw new ArgumentException("PublicKeyPem cannot be null or empty.", nameof(publicKeyPem));
            if (string.IsNullOrWhiteSpace(privateKeyPem)) throw new ArgumentException("PrivateKeyPem cannot be null or empty.", nameof(privateKeyPem));

            _publicKeyMap[keyId.Trim()] = publicKeyPem.Trim();
            _privateKeyMap[keyId.Trim()] = privateKeyPem.Trim();
        }

        public static (string keyId, string publicKeyPem, string privateKeyPem) GetOrCreateEphemeralKeyPair(string keyId = "config-signing-active-01")
        {
            lock (SyncLock)
            {
                if (EphemeralActivePrivateKeyPem == null || EphemeralActiveKeyId != keyId)
                {
                    using var rsa = RSA.Create(2048);
                    EphemeralActiveKeyId = keyId;
                    EphemeralActivePrivateKeyPem = rsa.ExportPkcs8PrivateKeyPem();
                    EphemeralActivePublicKeyPem = rsa.ExportSubjectPublicKeyInfoPem();
                }
                return (EphemeralActiveKeyId, EphemeralActivePublicKeyPem!, EphemeralActivePrivateKeyPem!);
            }
        }

        private static void EnsureEphemeralKeyPair()
        {
            if (EphemeralActivePrivateKeyPem == null)
            {
                GetOrCreateEphemeralKeyPair();
            }
        }
    }
}
