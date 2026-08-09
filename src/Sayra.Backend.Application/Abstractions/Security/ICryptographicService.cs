using System;

#nullable enable

namespace Sayra.Backend.Application.Abstractions.Security
{
    public interface ICryptographicService
    {
        // AES encryption/decryption
        byte[] EncryptAes256Cbc(byte[] plainText, byte[] key, byte[] iv);
        byte[] DecryptAes256Cbc(byte[] cipherText, byte[] key, byte[] iv);

        // HMAC SHA-256
        byte[] ComputeHmacSha256(byte[] data, byte[] key);
        bool VerifyHmacSha256(byte[] data, byte[] key, byte[] hash);

        // RSA Digital Signatures
        byte[] SignDataRsa(byte[] data, string privateKeyPem);
        bool VerifyDataRsa(byte[] data, byte[] signature, string publicKeyPem);
    }
}
