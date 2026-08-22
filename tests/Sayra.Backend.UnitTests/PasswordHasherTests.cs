using System;
using Microsoft.Extensions.Options;
using Sayra.Backend.Infrastructure.Configuration;
using Sayra.Backend.Infrastructure.Configuration.Options;
using Sayra.Backend.Infrastructure.Security;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class PasswordHasherTests
    {
        private readonly PasswordHasher _hasher = new();

        [Fact]
        public void HashPasswordWithDetails_ShouldUseArgon2id_AndGenerateSaltAndParams()
        {
            var password = "SecurePassword123!";

            var (hash, salt, algo, parameters) = _hasher.HashPasswordWithDetails(password);

            Assert.False(string.IsNullOrWhiteSpace(hash));
            Assert.False(string.IsNullOrWhiteSpace(salt));
            Assert.Equal("Argon2id", algo);
            Assert.Contains("DegreeOfParallelism", parameters);
            Assert.Contains("MemorySize", parameters);
        }

        [Fact]
        public void VerifyPassword_Argon2id_ShouldReturnTrueForCorrectPassword()
        {
            var password = "SecurePassword123!";
            var (hash, salt, algo, _) = _hasher.HashPasswordWithDetails(password);

            bool isValid = _hasher.VerifyPassword(password, hash, salt, algo);

            Assert.True(isValid);
        }

        [Fact]
        public void VerifyPassword_Argon2id_ShouldReturnFalseForWrongPassword()
        {
            var password = "SecurePassword123!";
            var (hash, salt, algo, _) = _hasher.HashPasswordWithDetails(password);

            bool isValid = _hasher.VerifyPassword("WrongPassword!", hash, salt, algo);

            Assert.False(isValid);
        }

        [Fact]
        public void HashPassword_TwoCalls_ShouldProduceUniqueSalts()
        {
            var password = "SamePassword123!";

            var (hash1, salt1) = _hasher.HashPassword(password);
            var (hash2, salt2) = _hasher.HashPassword(password);

            Assert.NotEqual(salt1, salt2);
            Assert.NotEqual(hash1, hash2);
        }

        [Fact]
        public void VerifyPassword_BackwardCompatiblePBKDF2_ShouldVerifySuccessfully()
        {
            var password = "LegacyPassword123!";
            var (hash, salt) = CreateLegacyPbkdf2Hash(password);

            bool isValid = _hasher.VerifyPassword(password, hash, salt, "PBKDF2");

            Assert.True(isValid);
            Assert.True(_hasher.NeedsRehash("PBKDF2"));
        }

        [Fact]
        public void NeedsRehash_ShouldReturnFalseForArgon2idWithCurrentParams()
        {
            Assert.False(_hasher.NeedsRehash("Argon2id"));
            Assert.True(_hasher.NeedsRehash("PBKDF2"));
            Assert.True(_hasher.NeedsRehash("SHA256"));
            Assert.True(_hasher.NeedsRehash(""));
        }

        [Fact]
        public void NeedsRehash_WithOutdatedParameters_ShouldReturnTrue()
        {
            string outdatedParams = "{\"DegreeOfParallelism\":1,\"MemorySize\":8192,\"Iterations\":1}";
            Assert.True(_hasher.NeedsRehash("Argon2id", outdatedParams));
        }

        [Fact]
        public void HashPassword_NullOrEmpty_ShouldThrowArgumentException()
        {
            Assert.Throws<ArgumentNullException>(() => _hasher.HashPasswordWithDetails(""));
            Assert.Throws<ArgumentNullException>(() => _hasher.HashPasswordWithDetails(null!));
        }

        [Fact]
        public void HashPassword_ExceedingMaxPasswordLength_ShouldThrowArgumentException()
        {
            string longPassword = new string('A', 200);
            Assert.Throws<ArgumentException>(() => _hasher.HashPasswordWithDetails(longPassword));
        }

        [Fact]
        public void VerifyPassword_ExceedingMaxPasswordLength_ShouldReturnFalse()
        {
            var (hash, salt, algo, _) = _hasher.HashPasswordWithDetails("ValidPassword123!");
            string longPassword = new string('A', 200);

            Assert.False(_hasher.VerifyPassword(longPassword, hash, salt, algo));
        }

        [Fact]
        public void VerifyPassword_MalformedHashOrSalt_ShouldFailClosed()
        {
            Assert.False(_hasher.VerifyPassword("Password123!", "not-base64-hash!!!", "not-base64-salt!!!", "Argon2id"));
        }

        [Fact]
        public void VerifyPassword_UnsupportedAlgorithm_ShouldFailClosed()
        {
            var (hash, salt, _, _) = _hasher.HashPasswordWithDetails("Password123!");
            Assert.False(_hasher.VerifyPassword("Password123!", hash, salt, "UNSUPPORTED_ALGO"));
        }

        [Fact]
        public void ConfigurationValidator_InvalidSecurityOptions_ShouldThrow()
        {
            var invalidOptions = new SecurityOptions
            {
                ArgonMemorySizeKb = 100 // Less than min 8192
            };

            Assert.Throws<InvalidOperationException>(() => ConfigurationValidator.ValidateSecurityOptions(invalidOptions));
        }

        private static (string Hash, string Salt) CreateLegacyPbkdf2Hash(string password)
        {
            byte[] saltBytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
            byte[] hashBytes = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
                password,
                saltBytes,
                10000,
                System.Security.Cryptography.HashAlgorithmName.SHA256,
                32);

            return (System.Convert.ToBase64String(hashBytes), System.Convert.ToBase64String(saltBytes));
        }
    }
}
