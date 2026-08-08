# Security Architecture

This document outlines the cryptographic controls, trust models, authentication, and secure auditing strategies for the **SAYRA Central Backend**.

---

## 1. Network Cryptography (TLS 1.3)

All communication between the SAYRA Client and Central Backend is encrypted in-transit.

### 1.1. HTTPS REST API & TCP Socket Encryption
* **Standard**: Both HTTP and custom TCP socket layers mandate **TLS 1.3**. Legacy protocols (TLS 1.0, 1.1, 1.2) are disabled to protect against downgrade attacks.
* **Cipher Suites**: High-strength AEAD cipher suites are strictly enforced:
  * `TLS_AES_256_GCM_SHA384`
  * `TLS_CHACHA20_POLY1305_SHA256`
  * `TLS_AES_128_GCM_SHA256`

### 1.2. PKI Trust Model
* Because the SAYRA Central Backend is designed to run in offline-first, local LAN environments, trusting external commercial Certificate Authorities (like Let's Encrypt) is not always feasible.
* **Local Root CA**: The backend system provisions and maintains its own private, self-managed Root Certificate Authority (Root CA) upon initial server installation.
* **Certificate Provisioning**:
  * The backend automatically generates and signs domain-specific certificates for the server's local IP or domain name.
  * Workstations during deployment are securely bootstrapped with the local Root CA certificate installed in their OS trust stores, ensuring the Client trusts the backend's self-signed TLS 1.3 certificates.

---

## 2. Authentication & Authorization

### 2.1. API Authentication
* **Admin / Management**: Secure JSON Web Tokens (JWT) using `RS256` signatures (RSA-2048/4096) or `ES256` (ECDSA).
* **Workstation Handshake**:
  * Workstations authenticate with the backend via a cryptographic challenge-response protocol.
  * During registration, each workstation receives a unique, long-lived workstation token, which is stored securely in the workstation's TPM or registry.
  * This token is verified via custom ASP.NET Core Authentication Handlers.

### 2.2. Replay Protection
* Every security-critical message envelope, especially UDP discovery responses and client action commands, must contain:
  1. A unique, high-entropy cryptographic `nonce` (validated against a Redis Bloom filter or a sliding-window cache).
  2. A high-resolution UTC `timestamp`. Timestamps drifting more than 10 seconds from the server's authoritative clock are rejected immediately.
  3. A digital signature covering the entire message payload.

---

## 3. Auditing and Tamper Resistance

### 3.1. Secure Audit Logging
* **Immutable Audit Trail**: Security-critical events (login attempts, remote commands, financial adjustments, configuration modifications) are recorded in an append-only `AuditLog` table.
* **Integrity Protection**:
  * Every audit entry contains a cryptographic hash of its payload combined with the hash of the preceding block (forming an audit log blockchain/hash chain).
  * Any database tamper/deletion is instantly detected during daily automated integrity checks.

---

## 4. Key Decisions & Open Questions Pending Client Reverse-Engineering

The following security choices cannot be fully finalized at this stage because they depend on verifying the existing compiled Client's exact hardcoded protocols:

1. **Client Signature Schema**: Does the existing client expect a specific digital signature format (e.g., RSA-2048 with SHA-256 vs. ECDSA secp256r1) for UDP Discovery?
2. **Client Private Key Storage**: Does the client possess a pre-configured embedded public key for validating backend server authenticity?
3. **HTTP Header Formatting**: Does the client require custom auth header formats (e.g., `X-SAYRA-TOKEN` vs. standard RFC 6750 `Authorization: Bearer <token>`)?
