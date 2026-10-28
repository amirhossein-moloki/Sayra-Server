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

            if (normA.Length != normB.Length || normA.Length % 2 != 0)
            {
                return false;
            }

            try
            {
                // Performance Optimization: Use stackalloc byte spans with fast zero-allocation hex decoding
                // to decode and compare hex strings in constant time without heap byte[] allocations.
                int byteCount = normA.Length / 2;
                Span<byte> bytesA = byteCount <= 128 ? stackalloc byte[byteCount] : new byte[byteCount];
                Span<byte> bytesB = byteCount <= 128 ? stackalloc byte[byteCount] : new byte[byteCount];

                if (!TryHexToBytes(normA, bytesA, out int writtenA) ||
                    !TryHexToBytes(normB, bytesB, out int writtenB) ||
                    writtenA != writtenB)
                {
                    return false;
                }

                return CryptographicOperations.FixedTimeEquals(bytesA[..writtenA], bytesB[..writtenB]);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryHexToBytes(ReadOnlySpan<char> hex, Span<byte> bytes, out int bytesWritten)
        {
            bytesWritten = 0;
            if (hex.Length % 2 != 0 || bytes.Length < hex.Length / 2)
            {
                return false;
            }

            for (int i = 0; i < hex.Length; i += 2)
            {
                int hi = GetHexVal(hex[i]);
                int lo = GetHexVal(hex[i + 1]);
                if (hi < 0 || lo < 0)
                {
                    return false;
                }
                bytes[bytesWritten++] = (byte)((hi << 4) | lo);
            }
            return true;
        }

        private static int GetHexVal(char hex)
        {
            int val = hex;
            if (val >= '0' && val <= '9') return val - '0';
            if (val >= 'a' && val <= 'f') return val - 'a' + 10;
            if (val >= 'A' && val <= 'F') return val - 'A' + 10;
            return -1;
        }
    }
}
