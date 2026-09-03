# API & Protocol Specifications

This document defines the multi-protocol communication contracts exposed by the **SAYRA Central Backend**: REST APIs, TLS 1.3 TCP Socket framing, UDP LAN Discovery, and authoritative Configuration Sync API.

---

## 1. Protocol Architecture Summary

| Protocol | Port | Transport / Encoding | Primary Responsibility | Authentication |
|---|---|---|---|---|
| **HTTPS REST API** | `5001` / `443` | HTTP/1.1 or HTTP/2, JSON | Admin UI, Account Deposits, Gamers, Configuration Sync | JWT Bearer / Custom Session Header |
| **TCP Socket Server** | `37021` | TLS 1.3, Custom Frame | Persistent client socket, Heartbeats, Remote Commands, Telemetry | Session Challenge-Response + HMAC-SHA256 |
| **UDP Discovery Server** | `37020` | UDP Broadcast, JSON | Local LAN Server Discovery | RSA-2048 Digital Signature |

---

## 2. HTTPS REST API Endpoints

### 2.1. Authentication & Identity (`/api/auth`, `/api/gamers`)
* `POST /api/auth/login`: Authenticate admin or gamer credentials, return JWT Bearer token and session metadata.
* `POST /api/auth/logout`: Revoke active authentication session token in PostgreSQL and Redis.
* `GET /api/auth/me`: Retrieve current caller `UserPrincipal` context and active permissions.
* `POST /api/gamers/authenticate`: Authenticate gamer credentials (legacy client contract).

### 2.2. Financial & Account Operations (`/api/accounts`)
* `POST /api/accounts/{gamerId}/deposit`: Deposit funds into gamer account with mandatory `IdempotencyKey`.
* `GET /api/accounts/{gamerId}/balance`: Query authoritative account balance and credit limit.
* `GET /api/accounts/{gamerId}/ledger`: Retrieve paginated financial ledger transactions.

### 2.3. Authoritative Configuration Synchronization (`/api/config`)
* `GET /api/config/package`: Authoritative workstation configuration synchronization query (`SynchronizeConfigurationQuery`).
  * **Parameters**: `currentVersion` (long?), `checksum` (string?).
  * **Behavior**: Resolves workstation context from `UserPrincipal.PcId`. Returns `304 Not Modified` with ETag if unchanged, safe JSON patch delta package if valid, or full configuration package if missing/stale.
* `GET /api/config/workstations/{workstationId}/effective`: Query effective resolved configuration with field trace sources.
* `POST /api/config/publications`: Administrative configuration lifecycle endpoints (Prepare, Publish, Activate, Revoke, Rollback).

---

## 3. TCP Socket Framing & Message Protocol (Port 37021)

### 3.1. Frame Layout
TCP socket byte streams use length-prefixed framing enforced by `TcpFrameParser`:
```text
┌───────────────────────────┬──────────────────────────────────────────┐
│ Payload Length (4 Bytes)  │ SecureMessageEnvelope JSON Payload       │
│ Big-Endian Int32          │ (UTF-8 Encoded JSON)                     │
└───────────────────────────┴──────────────────────────────────────────┘
```

### 3.2. `SecureMessageEnvelope` Structure
```json
{
  "messageId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "correlationId": "8a71f00a-1a33-401d-93d3-7d7120a11223",
  "sessionId": "b8f2c502-140a-430c-843e-0808b8e05c31",
  "sequenceNumber": 1042,
  "messageType": "HEARTBEAT_PING",
  "protocolVersion": "1.0",
  "timestamp": "2026-09-01T12:00:00.000Z",
  "payload": "{\"status\":\"OK\"}",
  "signature": "a3b8f1... (HMAC-SHA256 hex string)"
}
```

---

## 4. UDP LAN Server Discovery (Port 37020)

### 4.1. Request Broadcast
Client workstations broadcast a UDP packet to `255.255.255.255:37020`:
```json
{
  "protocol": "SAYRA_DISCOVERY_REQ",
  "clientVersion": "1.0.0",
  "nonce": "c9a8f102... (Random Hex)"
}
```

### 4.2. Server Response
`UdpDiscoveryServer` responds directly to the client's UDP endpoint with signed server transport parameters:
```json
{
  "protocol": "SAYRA_DISCOVERY_RESP",
  "serverId": "00000000-0000-0000-0000-000000000001",
  "tcpPort": 37021,
  "restPort": 5001,
  "tlsEnabled": true,
  "nonce": "c9a8f102...",
  "signature": "b9f3... (RSA-2048 SHA-256 signature over payload + nonce)"
}
```
