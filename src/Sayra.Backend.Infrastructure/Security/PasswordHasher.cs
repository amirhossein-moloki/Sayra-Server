using System;
using System.Security.Cryptography;
using Sayra.Backend.Application.Abstractions.Security;

namespace Sayra.Backend.Infrastructure.Security
{
    public class PasswordHasher : IPasswordHasher
    {
        private const int SaltSize = 32; // 256 bits
        private const int KeySize = 32;  // 256 bits
        private const int Iterations = 10000;

        public (string Hash, string Salt) HashPassword(string password)
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                throw new ArgumentNullException(nameof(password), "Password cannot be null or empty.");
            }

            byte[] saltBytes = RandomNumberGenerator.GetBytes(SaltSize);
            byte[] hashBytes = Rfc2898DeriveBytes.Pbkdf2(
                password,
                saltBytes,
                Iterations,
                HashAlgorithmName.SHA256,
                KeySize);

            return (Convert.ToBase64String(hashBytes), Convert.ToBase64String(saltBytes));
        }

        public bool VerifyPassword(string password, string hash, string salt)
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

            byte[] computedHashBytes = Rfc2898DeriveBytes.Pbkdf2(
                password,
                saltBytes,
                Iterations,
                HashAlgorithmName.SHA256,
                KeySize);

            return CryptographicOperations.FixedTimeEquals(expectedHashBytes, computedHashBytes);
        }
    }
}
