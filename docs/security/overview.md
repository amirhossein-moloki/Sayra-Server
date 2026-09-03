# Security, Authentication & Auditing Architecture

This document details the transport cryptography, authentication mechanisms, authorization boundaries, replay protection, and tamper-evident audit logging of the **SAYRA Central Backend**.

---

## 1. Network Cryptography & Local PKI

### 1.1. TLS 1.3 Transport Encryption
* Both HTTPS REST endpoints and custom TCP socket channels mandate **TLS 1.3**. Legacy protocols (TLS 1.0, 1.1, 1.2) are disabled to prevent downgrade attacks.
* High-strength AEAD cipher suites are enforced:
  * `TLS_AES_256_GCM_SHA384`
  * `TLS_CHACHA20_POLY1305_SHA256`
  * `TLS_AES_128_GCM_SHA256`

### 1.2. Local Root CA (Offline PKI)
* To support offline LAN gaming centers without internet access to external Certificate Authorities (like Let's Encrypt), the backend incorporates a self-managed **Local Root Certificate Authority (Local Root CA)**.
* Upon first boot, `LocalPkiService` provisions an RSA-4096 Root CA certificate and issues server certificates for local IP / hostname bindings. Workstations install the Root CA in their OS certificate store during deployment.

---

## 2. Authentication & Authorization

### 2.1. API & Gamer Authentication
* **Password Hashing**: Uses **Argon2id** (via `Konscious.Security.Cryptography.Argon2id`) as the primary password hashing mechanism. Supports backward-compatible verification for legacy `PBKDF2` hashes, automatically rehashing credentials to Argon2id upon successful authentication (`UserCredential`).
* **Session Tokens**: JWT Bearer tokens and opaque `SessionToken` records (`AuthenticationSession`) represent authenticated sessions. Revocation is enforced via Redis cache and PostgreSQL tracking (`LogoutCommandHandler`, `DeactivateGamerCommandHandler`).

### 2.2. Role-Based & Resource-Level Authorization (RBAC)
* **RBAC & Permission Catalog**: `[HasPermission(...)]` filter evaluates active permissions assigned across active user roles (`UserPrincipalMiddleware`).
* **Resource-Level Access Control (`UserResourceAccess`)**: Evaluates explicit resource grants and denials across individual user and role assignments (`POST /api/auth/resource-check`), enforcing organizational boundaries (Organization ➔ Site ➔ Zone ➔ Workstation).

---

## 3. Communication Security & Replay Protection

### 3.1. Message Envelope & HMAC Signatures
All persistent TCP socket messages wrap payloads in a `SecureMessageEnvelope`:
* **Signature**: HMAC-SHA256 calculated over canonical header fields (`MessageId`, `CorrelationId`, `SessionId`, `SequenceNumber`, `MessageType`, `Timestamp`, `Payload`).
* **Constant-Time Verification**: Verified using `CryptographicOperations.FixedTimeEquals` to prevent timing side-channel attacks.

### 3.2. Sequence Tracking & Replay Protection
* **Sequence Validation (`ISequenceValidator`)**: Tracks per-session outbound sequence numbers and enforces inbound sliding window replay protection with seen message ID deduplication.
* **Timestamp Tolerance**: Messages with server timestamp drift exceeding 10 seconds are rejected immediately (`AUTHENTICATION_PROTOCOL_VIOLATION`).

---

## 4. Tamper-Resistant Audit Logging & Sensitive Data Redaction

### 4.1. Sensitive Log Redaction
* `ISecurityEventService` and Serilog logging enrichers sanitize security logs using regex-based redaction patterns, masking passwords, Bearer tokens, private keys, and session secrets before persisting to PostgreSQL `AuditEvents` or stdout.

### 4.2. Hash-Chained Security Event Log
* Security audit events (`CONFIG_PUBLISHED`, `CONFIG_REVOKED`, `CONFIG_ROLLBACK`, `AUTHENTICATION_FAILED`, `ACCESS_DENIED`) are appended to the `AuditEvents` table.
* Entries combine payload SHA-256 hashes with the hash of the preceding record, establishing a tamper-evident audit log chain that detects unauthorized database modifications.
