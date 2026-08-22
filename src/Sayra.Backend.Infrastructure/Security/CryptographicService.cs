using System;
using System.IO;
using System.Security.Cryptography;
using Sayra.Backend.Application.Abstractions.Security;

namespace Sayra.Backend.Infrastructure.Security
{
    public class CryptographicService : ICryptographicService
    {
        public byte[] EncryptAes256Cbc(byte[] plainText, byte[] key, byte[] iv)
        {
            if (plainText == null) throw new ArgumentNullException(nameof(plainText));
            if (key == null || key.Length != 32) throw new ArgumentException("Key must be 256 bits (32 bytes).", nameof(key));
            if (iv == null || iv.Length != 16) throw new ArgumentException("IV must be 128 bits (16 bytes).", nameof(iv));

            // Performance: Use .NET 8 aes.EncryptCbc to perform direct zero-stream-allocation CBC encryption,
            // eliminating MemoryStream and CryptoStream object overhead on high-frequency TCP stream traffic.
            using (var aes = Aes.Create())
            {
                aes.Key = key;
                return aes.EncryptCbc(plainText, iv, PaddingMode.PKCS7);
            }
        }

        public byte[] DecryptAes256Cbc(byte[] cipherText, byte[] key, byte[] iv)
        {
            if (cipherText == null) throw new ArgumentNullException(nameof(cipherText));
            if (key == null || key.Length != 32) throw new ArgumentException("Key must be 256 bits (32 bytes).", nameof(key));
            if (iv == null || iv.Length != 16) throw new ArgumentException("IV must be 128 bits (16 bytes).", nameof(iv));

            // Performance: Use .NET 8 aes.DecryptCbc to perform direct zero-stream-allocation CBC decryption,
            // avoiding intermediate stream buffers and GC pressure for every incoming message frame.
            using (var aes = Aes.Create())
            {
                aes.Key = key;
                return aes.DecryptCbc(cipherText, iv, PaddingMode.PKCS7);
            }
        }

        public byte[] ComputeHmacSha256(byte[] data, byte[] key)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (key == null) throw new ArgumentNullException(nameof(key));

            // Performance: Use .NET 8 static HMACSHA256.HashData to compute HMAC-SHA256 directly without
            // instantiating or disposing disposable HMACSHA256 object instances.
            return HMACSHA256.HashData(key, data);
        }

        public bool VerifyHmacSha256(byte[] data, byte[] key, byte[] hash)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (hash == null) throw new ArgumentNullException(nameof(hash));

            var computed = ComputeHmacSha256(data, key);
            return CryptographicEquals(computed, hash);
        }

        public byte[] SignDataRsa(byte[] data, string privateKeyPem)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (string.IsNullOrWhiteSpace(privateKeyPem)) throw new ArgumentException("Private key PEM cannot be null or empty.", nameof(privateKeyPem));

            using (var rsa = RSA.Create())
            {
                rsa.ImportFromPem(privateKeyPem);
                return rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }
        }

        public bool VerifyDataRsa(byte[] data, byte[] signature, string publicKeyPem)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (signature == null) throw new ArgumentNullException(nameof(signature));
            if (string.IsNullOrWhiteSpace(publicKeyPem)) throw new ArgumentException("Public key PEM cannot be null or empty.", nameof(publicKeyPem));

            using (var rsa = RSA.Create())
            {
                rsa.ImportFromPem(publicKeyPem);
                return rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }
        }

        private static bool CryptographicEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null) return a == b;
            // Performance: Use SIMD-optimized CryptographicOperations.FixedTimeEquals for constant-time comparison
            return CryptographicOperations.FixedTimeEquals(a, b);
        }
    }
}
