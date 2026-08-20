## 2026-08-19 - Zero-allocation static cryptographic methods in .NET 8
**Learning:** High-frequency cryptographic operations (AES CBC encryption/decryption, HMAC-SHA256 signature calculation) in .NET 8 can be optimized to eliminate memory streams, crypto streams, and object instantiations using static methods `Aes.EncryptCbc`, `Aes.DecryptCbc`, and `HMACSHA256.HashData`.
**Action:** Always prefer `HMACSHA256.HashData` and `Aes.EncryptCbc`/`Aes.DecryptCbc` over instantiating `HMACSHA256` or `CryptoStream` for hot-path byte array crypto routines.
