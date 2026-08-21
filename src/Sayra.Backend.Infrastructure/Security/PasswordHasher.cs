using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Konscious.Security.Cryptography;
using Sayra.Backend.Application.Abstractions.Security;

namespace Sayra.Backend.Infrastructure.Security
{
    public class PasswordHasher : IPasswordHasher
    {
        private const int SaltSize = 16; // 128 bits
        private const int KeySize = 32;  // 256 bits
        private const int ArgonDegreeOfParallelism = 2;
        private const int ArgonMemorySizeKb = 19456; // 19MB
        private const int ArgonIterations = 2;

        private const int Pbkdf2Iterations = 10000;
        private const int Pbkdf2KeySize = 32;

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

            byte[] saltBytes = RandomNumberGenerator.GetBytes(SaltSize);
            byte[] hashBytes = ComputeArgon2id(password, saltBytes, ArgonDegreeOfParallelism, ArgonMemorySizeKb, ArgonIterations, KeySize);

            var parametersJson = JsonSerializer.Serialize(new
            {
                DegreeOfParallelism = ArgonDegreeOfParallelism,
                MemorySize = ArgonMemorySizeKb,
                Iterations = ArgonIterations
            });

            return (
                Convert.ToBase64String(hashBytes),
                Convert.ToBase64String(saltBytes),
                "Argon2id",
                parametersJson
            );
        }

        public bool VerifyPassword(string password, string hash, string salt)
        {
            return VerifyPassword(password, hash, salt, "Argon2id");
        }

        public bool VerifyPassword(string password, string hash, string salt, string algorithm)
        {
            if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(hash) || string.IsNullOrWhiteSpace(salt))
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
                byte[] computedPbkdf2 = Rfc2898DeriveBytes.Pbkdf2(
                    password,
                    saltBytes,
                    Pbkdf2Iterations,
                    HashAlgorithmName.SHA256,
                    Pbkdf2KeySize);

                return CryptographicOperations.FixedTimeEquals(expectedHashBytes, computedPbkdf2);
            }

            // Default to Argon2id
            try
            {
                byte[] computedArgon = ComputeArgon2id(password, saltBytes, ArgonDegreeOfParallelism, ArgonMemorySizeKb, ArgonIterations, expectedHashBytes.Length);
                return CryptographicOperations.FixedTimeEquals(expectedHashBytes, computedArgon);
            }
            catch
            {
                return false;
            }
        }

        public bool NeedsRehash(string algorithm)
        {
            if (string.IsNullOrWhiteSpace(algorithm)) return true;
            return !string.Equals(algorithm, "Argon2id", StringComparison.OrdinalIgnoreCase);
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
