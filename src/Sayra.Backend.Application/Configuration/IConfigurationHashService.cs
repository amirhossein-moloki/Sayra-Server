using System;

#nullable enable

namespace Sayra.Backend.Application.Configuration
{
    /// <summary>
    /// Contract for calculating and verifying SHA-256 cryptographic hashes over canonical configuration content.
    /// </summary>
    public interface IConfigurationHashService
    {
        /// <summary>
        /// Computes SHA-256 hash over canonical UTF-8 bytes and returns lowercase hexadecimal representation.
        /// </summary>
        string ComputeHash(byte[] canonicalBytes);

        /// <summary>
        /// Computes SHA-256 hash over canonical JSON string and returns lowercase hexadecimal representation.
        /// </summary>
        string ComputeHash(string canonicalJson);

        /// <summary>
        /// Verifies whether the computed SHA-256 hash over canonical bytes matches the expected hash (constant-time comparison).
        /// </summary>
        bool VerifyHash(byte[] canonicalBytes, string expectedHash);

        /// <summary>
        /// Verifies whether the computed SHA-256 hash over canonical JSON string matches the expected hash (constant-time comparison).
        /// </summary>
        bool VerifyHash(string canonicalJson, string expectedHash);
    }
}
