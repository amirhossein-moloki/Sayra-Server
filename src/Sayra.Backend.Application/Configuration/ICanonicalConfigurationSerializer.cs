using System;

#nullable enable

namespace Sayra.Backend.Application.Configuration
{
    /// <summary>
    /// Contract for deterministic canonical configuration serialization.
    /// Guarantees consistent property sorting, UTF-8 encoding, and deterministic
    /// value formatting across logical configuration instances.
    /// </summary>
    public interface ICanonicalConfigurationSerializer
    {
        /// <summary>
        /// Serializes an object or raw JSON payload string into a deterministic canonical JSON string.
        /// </summary>
        string SerializeToCanonicalJson(object modelOrPayload);

        /// <summary>
        /// Serializes a raw JSON string into a deterministic canonical JSON string.
        /// </summary>
        string SerializeToCanonicalJson(string rawJsonPayload);

        /// <summary>
        /// Serializes an object or raw JSON payload string into deterministic UTF-8 canonical bytes.
        /// </summary>
        byte[] SerializeToCanonicalBytes(object modelOrPayload);

        /// <summary>
        /// Serializes a raw JSON string into deterministic UTF-8 canonical bytes.
        /// </summary>
        byte[] SerializeToCanonicalBytes(string rawJsonPayload);
    }
}
