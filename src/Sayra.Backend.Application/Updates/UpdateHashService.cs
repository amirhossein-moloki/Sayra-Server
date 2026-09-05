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

            if (!CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(normalizedCalculated),
                System.Text.Encoding.UTF8.GetBytes(normalizedDeclared)))
            {
                throw new InvalidDomainException("HASH_MISMATCH", $"Calculated SHA-256 hash '{normalizedCalculated}' does not match declared hash '{normalizedDeclared}'.");
            }
        }
    }
}
