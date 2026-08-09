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

            using (var aes = Aes.Create())
            {
                aes.Key = key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using (var encryptor = aes.CreateEncryptor(aes.Key, aes.IV))
                using (var ms = new MemoryStream())
                {
                    using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                    {
                        cs.Write(plainText, 0, plainText.Length);
                        cs.FlushFinalBlock();
                    }
                    return ms.ToArray();
                }
            }
        }

        public byte[] DecryptAes256Cbc(byte[] cipherText, byte[] key, byte[] iv)
        {
            if (cipherText == null) throw new ArgumentNullException(nameof(cipherText));
            if (key == null || key.Length != 32) throw new ArgumentException("Key must be 256 bits (32 bytes).", nameof(key));
            if (iv == null || iv.Length != 16) throw new ArgumentException("IV must be 128 bits (16 bytes).", nameof(iv));

            using (var aes = Aes.Create())
            {
                aes.Key = key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using (var decryptor = aes.CreateDecryptor(aes.Key, aes.IV))
                using (var ms = new MemoryStream())
                {
                    using (var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Write))
                    {
                        cs.Write(cipherText, 0, cipherText.Length);
                        cs.FlushFinalBlock();
                    }
                    return ms.ToArray();
                }
            }
        }

        public byte[] ComputeHmacSha256(byte[] data, byte[] key)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (key == null) throw new ArgumentNullException(nameof(key));

            using (var hmac = new HMACSHA256(key))
            {
                return hmac.ComputeHash(data);
            }
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
            if (a.Length != b.Length) return false;
            int result = 0;
            for (int i = 0; i < a.Length; i++)
            {
                result |= a[i] ^ b[i];
            }
            return result == 0;
        }
    }
}
