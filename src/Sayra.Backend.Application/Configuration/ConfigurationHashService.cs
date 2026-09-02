using System;
using System.Security.Cryptography;
using System.Text;

#nullable enable

namespace Sayra.Backend.Application.Configuration
{
    /// <summary>
    /// Production implementation of SHA-256 configuration hash service.
    /// Uses .NET 8 zero-allocation SHA256.HashData and CryptographicOperations.FixedTimeEquals
    /// for secure constant-time verification.
    /// </summary>
    public class ConfigurationHashService : IConfigurationHashService
    {
        private readonly ICanonicalConfigurationSerializer _serializer;

        public ConfigurationHashService(ICanonicalConfigurationSerializer serializer)
        {
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        }

        public ConfigurationHashService()
            : this(new CanonicalConfigurationSerializer())
        {
        }

        public string ComputeHash(byte[] canonicalBytes)
        {
            if (canonicalBytes == null || canonicalBytes.Length == 0)
            {
                throw new ArgumentException("Canonical bytes cannot be null or empty.", nameof(canonicalBytes));
            }

            byte[] hashBytes = SHA256.HashData(canonicalBytes);
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        public string ComputeHash(string canonicalJson)
        {
            if (string.IsNullOrWhiteSpace(canonicalJson))
            {
                throw new ArgumentException("Canonical JSON string cannot be null or empty.", nameof(canonicalJson));
            }

            // Ensure canonicalization
            byte[] bytes = _serializer.SerializeToCanonicalBytes(canonicalJson);
            return ComputeHash(bytes);
        }

        public bool VerifyHash(byte[] canonicalBytes, string expectedHash)
        {
            if (canonicalBytes == null || string.IsNullOrWhiteSpace(expectedHash))
            {
                return false;
            }

            string computedHash = ComputeHash(canonicalBytes);
            return ConstantTimeEqualsHex(computedHash, expectedHash);
        }

        public bool VerifyHash(string canonicalJson, string expectedHash)
        {
            if (string.IsNullOrWhiteSpace(canonicalJson) || string.IsNullOrWhiteSpace(expectedHash))
            {
                return false;
            }

            string computedHash = ComputeHash(canonicalJson);
            return ConstantTimeEqualsHex(computedHash, expectedHash);
        }

        private static bool ConstantTimeEqualsHex(string hashA, string hashB)
        {
            if (string.IsNullOrWhiteSpace(hashA) || string.IsNullOrWhiteSpace(hashB))
            {
                return false;
            }

            // Compare case-insensitively
            var normA = hashA.Trim().ToLowerInvariant();
            var normB = hashB.Trim().ToLowerInvariant();

            if (normA.Length != normB.Length)
            {
                return false;
            }

            try
            {
                byte[] bytesA = Convert.FromHexString(normA);
                byte[] bytesB = Convert.FromHexString(normB);
                return CryptographicOperations.FixedTimeEquals(bytesA, bytesB);
            }
            catch
            {
                return false;
            }
        }
    }
}
