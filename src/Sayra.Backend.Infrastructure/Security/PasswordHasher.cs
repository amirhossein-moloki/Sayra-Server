using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Konscious.Security.Cryptography;
using Microsoft.Extensions.Options;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Infrastructure.Configuration.Options;

namespace Sayra.Backend.Infrastructure.Security
{
    public class PasswordHasher : IPasswordHasher
    {
        private readonly SecurityOptions _options;

        public PasswordHasher(IOptions<SecurityOptions>? options = null)
        {
            _options = options?.Value ?? new SecurityOptions();
        }

        public (string Hash, string Salt) HashPassword(string password)
        {
            var details = HashPasswordWithDetails(password);
            return (details.Hash, details.Salt);
        }

        public (string Hash, string Salt, string Algorithm, string Parameters) HashPasswordWithDetails(string password)
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                throw new ArgumentNullException(nameof(password), "Password cannot be null or empty.");
            }

            if (password.Length > _options.MaxPasswordLength)
            {
                throw new ArgumentException($"Password length exceeds maximum allowed length of {_options.MaxPasswordLength} characters.", nameof(password));
            }

            byte[] saltBytes = RandomNumberGenerator.GetBytes(_options.SaltSize);
            byte[] hashBytes = ComputeArgon2id(
                password,
                saltBytes,
                _options.ArgonDegreeOfParallelism,
                _options.ArgonMemorySizeKb,
                _options.ArgonIterations,
                _options.KeySize);

            var parametersJson = JsonSerializer.Serialize(new
            {
                DegreeOfParallelism = _options.ArgonDegreeOfParallelism,
                MemorySize = _options.ArgonMemorySizeKb,
                Iterations = _options.ArgonIterations
            });

            return (
                Convert.ToBase64String(hashBytes),
                Convert.ToBase64String(saltBytes),
                _options.PasswordHashAlgorithm,
                parametersJson
            );
        }

        public bool VerifyPassword(string password, string hash, string salt)
        {
            return VerifyPassword(password, hash, salt, _options.PasswordHashAlgorithm);
        }

        public bool VerifyPassword(string password, string hash, string salt, string algorithm)
        {
            if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(hash) || string.IsNullOrWhiteSpace(salt))
            {
                return false;
            }

            if (password.Length > _options.MaxPasswordLength)
            {
                return false;
            }

            byte[] saltBytes;
            byte[] expectedHashBytes;
            try
            {
                saltBytes = Convert.FromBase64String(salt);
                expectedHashBytes = Convert.FromBase64String(hash);
            }
            catch (FormatException)
            {
                return false;
            }

            if (string.Equals(algorithm, "PBKDF2", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    byte[] computedPbkdf2 = Rfc2898DeriveBytes.Pbkdf2(
                        password,
                        saltBytes,
                        _options.Pbkdf2Iterations,
                        HashAlgorithmName.SHA256,
                        expectedHashBytes.Length > 0 ? expectedHashBytes.Length : _options.KeySize);

                    return CryptographicOperations.FixedTimeEquals(expectedHashBytes, computedPbkdf2);
                }
                catch
                {
                    return false;
                }
            }

            if (string.Equals(algorithm, "Argon2id", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    byte[] computedArgon = ComputeArgon2id(
                        password,
                        saltBytes,
                        _options.ArgonDegreeOfParallelism,
                        _options.ArgonMemorySizeKb,
                        _options.ArgonIterations,
                        expectedHashBytes.Length);

                    return CryptographicOperations.FixedTimeEquals(expectedHashBytes, computedArgon);
                }
                catch
                {
                    return false;
                }
            }

            // Unsupported algorithm fails closed
            return false;
        }

        public bool NeedsRehash(string algorithm)
        {
            return NeedsRehash(algorithm, null);
        }

        public bool NeedsRehash(string algorithm, string? parameters)
        {
            if (string.IsNullOrWhiteSpace(algorithm)) return true;

            if (!string.Equals(algorithm, _options.PasswordHashAlgorithm, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(parameters))
            {
                try
                {
                    using var doc = JsonDocument.Parse(parameters);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("DegreeOfParallelism", out var pProp) && pProp.GetInt32() != _options.ArgonDegreeOfParallelism)
                        return true;
                    if (root.TryGetProperty("MemorySize", out var mProp) && mProp.GetInt32() != _options.ArgonMemorySizeKb)
                        return true;
                    if (root.TryGetProperty("Iterations", out var iProp) && iProp.GetInt32() != _options.ArgonIterations)
                        return true;
                }
                catch
                {
                    return true;
                }
            }

            return false;
        }

        private static byte[] ComputeArgon2id(string password, byte[] salt, int parallelism, int memoryKb, int iterations, int outputLength)
        {
            using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
            {
                Salt = salt,
                DegreeOfParallelism = parallelism,
                MemorySize = memoryKb,
                Iterations = iterations
            };

            return argon2.GetBytes(outputLength);
        }
    }
}
