# Stage 11-02 — Identity Flow Report

## 1. Executive Summary

This document describes the identity verification, authentication, handshake, and session invalidation flows across the SAYRA Central Backend platform. It provides sequence diagrams and architectural descriptions for both User HTTP API interactions and Client Workstation TCP/TLS connections.

---

## 2. User Authentication & Authorization Flow

### 2.1 User Login Sequence

```text
+----------+                  +-------------------+            +---------------+            +------------------+
|  User /  |                  | SAYRA Backend API |            | Password      |            | Redis / Postgres |
|  Client  |                  | (AuthController)  |            | Hasher        |            | Storage          |
+----+-----+                  +---------+---------+            +-------+-------+            +--------+---------+
     |                                  |                              |                             |
     | POST /api/v1/auth/login          |                              |                             |
     +--------------------------------->|                              |                             |
     | (Username, Password)             |                              |                             |
     |                                  | Get User & Credential Record |                             |
     |                                  +----------------------------------------------------------->|
     |                                  |<-----------------------------------------------------------+
     |                                  |                              |                             |
     |                                  | Verify Password (Argon2id)   |                             |
     |                                  +----------------------------->|                             |
     |                                  |<-----------------------------+                             |
     |                                  | [Valid Password]             |                             |
     |                                  |                              |                             |
     |                                  | Create Auth Session Token    |                             |
     |                                  +----------------------------------------------------------->|
     |                                  |                              |                             |
     | 200 OK (Token, ExpiresAt)        |                              |                             |
     |<---------------------------------+                              |                             |
     |                                  |                              |                             |
```

### 2.2 Invalid Credentials Failure Flow

```text
+----------+                  +-------------------+            +---------------+            +------------------+
|  User /  |                  | SAYRA Backend API |            | Password      |            | Security Event   |
|  Client  |                  | (AuthController)  |            | Hasher        |            | Service          |
+----+-----+                  +---------+---------+            +-------+-------+            +--------+---------+
     |                                  |                              |                             |
     | POST /api/v1/auth/login          |                              |                             |
     +--------------------------------->|                              |                             |
     | (Username, WrongPassword)        |                              |                             |
     |                                  | Verify Password              |                             |
     |                                  +----------------------------->|                             |
     |                                  |<-----------------------------+                             |
     |                                  | [Invalid Password]           |                             |
     |                                  |                              |                             |
     |                                  | Increment FailedLoginCount   |                             |
     |                                  | Record Security Event        |                             |
     |                                  +----------------------------------------------------------->|
     | 401 Unauthorized                 |                              |                             |
     | {"code": "AUTH_FAILED"}          |                              |                             |
     |<---------------------------------+                              |                             |
     |                                  |                              |                             |
```

---

## 3. Client Workstation Connection & Handshake Flow

### 3.1 Successful Workstation Authentication

```text
+-------------+               +-------------------+            +---------------------+       +-------------------+
| Workstation |               | TCP Server        |            | ClientAuthService   |       | Workstation       |
| Client      |               | Transport         |            |                     |       | Handler           |
+------+------+               +---------+---------+            +----------+----------+       +---------+---------+
       |                                |                                 |                            |
       | TCP Connect                    |                                 |                            |
       +------------------------------->|                                 |                            |
       |                                | State: Authenticating           |                            |
       |                                | Generate Challenge              |                            |
       |                                +-------------------------------->|                            |
       | Challenge (Base64)             |<--------------------------------+                            |
       |<-------------------------------+                                 |                            |
       |                                |                                 |                            |
       | AuthResponseDto                |                                 |                            |
       | (PcId, HMAC, EncryptedKey)     |                                 |                            |
       +------------------------------->| Validate HMAC & Decrypt Key     |                            |
       |                                +-------------------------------->|                            |
       |                                |                                 | Authorize & Bind Device    |
       |                                |                                 +--------------------------->|
       |                                |                                 |<---------------------------+
       |                                | Transition State: Authenticated |                            |
       | Auth Success (SessionReady)    |<--------------------------------+                            |
       |<-------------------------------+                                 |                            |
       |                                |                                 |                            |
```

### 3.2 Fake / Unauthorized Client Rejection Flow

```text
+-------------+               +-------------------+            +---------------------+       +-------------------+
| Fake Client |               | TCP Server        |            | ClientAuthService   |       | Security Event    |
| / Attacker  |               | Transport         |            |                     |       | Logger            |
+------+------+               +---------+---------+            +----------+----------+       +---------+---------+
       |                                |                                 |                            |
       | TCP Connect                    |                                 |                            |
       +------------------------------->| Challenge Generated             |                            |
       | Challenge (Base64)             |<--------------------------------+                            |
       |<-------------------------------+                                 |                            |
       |                                |                                 |                            |
       | AuthResponseDto (Invalid HMAC) |                                 |                            |
       +------------------------------->| Validate HMAC                   |                            |
       |                                +-------------------------------->|                            |
       |                                | [HMAC Verification Failed]      |                            |
       |                                |                                 | Record Security Event      |
       |                                |                                 +--------------------------->|
       | Connection Closed              |<--------------------------------+                            |
       |<-------------------------------+                                 |                            |
       |                                | Terminate Socket Connection     |                            |
       |                                |                                 |                            |
```

---

## 4. Token Invalidation & Logout Flow

```text
+----------+                  +-------------------+            +-------------------+        +-------------------+
|  User    |                  | UserPrincipal     |            | Auth Session      |        | Redis Cache /     |
|  Client  |                  | Middleware        |            | Service           |        | DB Repository     |
+----+-----+                  +---------+---------+            +---------+---------+        +---------+---------+
     |                                  |                                |                            |
     | POST /api/v1/auth/logout         |                                |                            |
     | (Bearer SessionToken)            |                                |                            |
     +--------------------------------->| Validate Token & Principal     |                            |
     |                                  +------------------------------->|                            |
     |                                  |<-------------------------------+                            |
     |                                  | Execute RevokeSessionAsync     |                            |
     |                                  +------------------------------->| Revoke DB Record           |
     |                                  |                                | Add to Redis Revoked List  |
     |                                  |                                +--------------------------->|
     | 200 OK                           |                                |                            |
     |<---------------------------------+                                |                            |
     |                                                                                                |
     | Subsequent Request using Revoked Token                                                         |
     +--------------------------------->| Check Redis Revoked List                                    |
     |                                  +------------------------------------------------------------>|
     |                                  |<------------------------------------------------------------+
     | 401 Unauthorized                 | [Token Revoked -> Fail Closed]                              |
     |<---------------------------------+                                                             |
     |                                  |                                                             |
```

---

## 5. Certification Statement

The Identity, Handshake, and Session flows of SAYRA Central Backend provide end-to-end security, constant-time challenge validation, immediate session revocation, and robust protection against unauthorized socket connections and header-forgery attacks.
