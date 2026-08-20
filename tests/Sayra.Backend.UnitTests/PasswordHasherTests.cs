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
            // Simulating PBKDF2 hash
            var (hash, salt) = CreateLegacyPbkdf2Hash(password);

            bool isValid = _hasher.VerifyPassword(password, hash, salt, "PBKDF2");

            Assert.True(isValid);
            Assert.True(_hasher.NeedsRehash("PBKDF2"));
        }

        [Fact]
        public void NeedsRehash_ShouldReturnFalseForArgon2id()
        {
            Assert.False(_hasher.NeedsRehash("Argon2id"));
            Assert.True(_hasher.NeedsRehash("PBKDF2"));
            Assert.True(_hasher.NeedsRehash("SHA256"));
            Assert.True(_hasher.NeedsRehash(""));
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
