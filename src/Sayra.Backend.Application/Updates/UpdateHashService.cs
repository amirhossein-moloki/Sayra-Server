using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Domain.Exceptions;

#nullable enable

namespace Sayra.Backend.Application.Updates
{
    public interface IUpdateHashService
    {
        Task<string> ComputeSha256Async(Stream stream, CancellationToken cancellationToken = default);
        void ValidateDeclaredHash(string calculatedSha256, string? declaredSha256);
    }

    public class UpdateHashService : IUpdateHashService
    {
        private const int BufferSize = 65536; // 64 KB

        public async Task<string> ComputeSha256Async(Stream stream, CancellationToken cancellationToken = default)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            if (stream.CanSeek)
            {
                stream.Position = 0;
            }

            using var incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[BufferSize];
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
            {
                incrementalHash.AppendData(buffer, 0, bytesRead);
            }

            var hashBytes = incrementalHash.GetHashAndReset();
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        public void ValidateDeclaredHash(string calculatedSha256, string? declaredSha256)
        {
            if (string.IsNullOrWhiteSpace(declaredSha256))
            {
                return; // Declared hash is optional
            }

            var normalizedCalculated = calculatedSha256.Trim().ToLowerInvariant();
            var normalizedDeclared = declaredSha256.Trim().ToLowerInvariant();

            if (normalizedCalculated.Length != 64 || normalizedDeclared.Length != 64)
            {
                throw new InvalidDomainException("INVALID_HASH_FORMAT", "SHA-256 hash must be 64 hexadecimal characters.");
            }

            // Performance Optimization: Use stackalloc byte spans with zero-allocation hex decoding
            // to decode and compare SHA-256 hex bytes in constant time without allocating heap byte[] arrays.
            Span<byte> bytesA = stackalloc byte[32];
            Span<byte> bytesB = stackalloc byte[32];

            if (!TryHexToBytes(normalizedCalculated, bytesA, out _) ||
                !TryHexToBytes(normalizedDeclared, bytesB, out _) ||
                !CryptographicOperations.FixedTimeEquals(bytesA, bytesB))
            {
                throw new InvalidDomainException("HASH_MISMATCH", $"Calculated SHA-256 hash '{normalizedCalculated}' does not match declared hash '{normalizedDeclared}'.");
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
