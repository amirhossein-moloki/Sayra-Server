# STAGE 01-06 — WORKSTATION REGISTRATION, PROVISIONING & DEVICE IDENTITY REPORT

## EXECUTIVE SUMMARY

This report documents the design, implementation, and verification of the **SAYRA Central Backend — Stage 01-06: Workstation Registration, Provisioning & Device Identity** module.

All core workstation identity rules, idempotent REST registration endpoints, authorization checks during TCP handshakes, connection-to-workstation binding, concurrent connection protection policies, and audit logging features have been successfully implemented.

The backend remains fully backward-compatible with the existing compiled SAYRA Client without requiring any client-side modifications. The entire solution is covered by a high-fidelity test suite comprising unit tests, database/Redis integration tests, and custom TLS 1.3/TCP authentication and binding integration tests.

---

## 1. WORKSTATION IDENTITY & DOMAIN MODEL

The `Workstation` entity has been extended to fulfill the Client contract and support clean state management:

- **Primary Identity**: The workstation identity is uniquely determined by `PcId` (normalizing inputs to upper-case, trimmed values).
- **Physical & State Metadata**:
  - `PcId` (Unique primary identity)
  - `SiteId` (Normalizing to upper-case, trimmed values)
  - `Hostname` (Physical PC machine name)
  - `MacAddress` (Normalized standard 6-octet colon-separated format, e.g., `AA:BB:CC:DD:EE:FF`)
  - `IpAddress` (Physical IP address format, validated natively)
  - `ClientVersion` & `OsVersion` (Version tracking metadata)
  - `Status` (Offline, Online, InUse, Maintenance)
  - `LastSeen` (Authoritative UTC timestamp)
  - `IsDisabled` (Boolean flag to prevent disabled workstations from connecting)
  - `RowVersion` (Optimistic concurrency token)

### 1.1. Status Transition State Machine
The `Workstation` entity implements strict domain-driven lifecycle status transition rules via the `TransitionTo(string newStatus)` method:
```text
  UNKNOWN (Not Registered)
     │
     ▼
  REGISTER (via API / POST)
     │
     ▼
  ONLINE (Handshake Successful)
    ├──► START SESSION ──► IN_USE
    └──► MAINTENANCE ────► MAINTENANCE
```
- **Invalid Transitions**:
  - Moving directly from `Offline` to `InUse` is blocked (must go to `Online` first).
  - Moving directly from `Maintenance` to `InUse` is blocked (must go to `Online` first).

### 1.2. Validation & Normalization
The `NormalizeAndValidate()` method executes prior to persistence:
- Sanitizes and upper-cases `PcId` and `SiteId`.
- Normalizes `MacAddress` to colon-separated uppercase format.
- Validates the structural integrity of both MAC and IP addresses, throwing a concrete `InvalidDomainException` on failures.

---

## 2. REST REGISTRATION API (`POST /api/clients`)

An idempotent, REST-compliant registration controller is implemented at `POST /api/clients`:

- **New Workstation**: Creates a new record with `Status = "Online"` and saves it to PostgreSQL.
- **Existing Workstation**: Updates its metadata (`SiteId`, `Hostname`, `MacAddress`, `IpAddress`, `ClientVersion`, `OsVersion`, `LastSeen = UtcNow`) safely and returns the updated entity without creating duplicate records.
- **Idempotency**: Repeated calls do not result in errors or duplicated entries.
- **MAC Uniqueness Rule**: If a MAC address is already registered under another `PcId`, a `400 Bad Request` with `DUPLICATE_MAC_ADDRESS` code is returned to prevent spoofing or identity duplication.

---

## 3. TCP TRANSPORT BOUNDARY & IDENTITY BINDING

The secure TCP/TLS 1.3 socket boundary has been fully integrated with device identity:

```text
TLS 1.3 Handshake Successful
              │
              ▼
[ Challenge-Response Authed ] (Proves possession of SAYRA_MASTER_KEY)
              │
              ▼
[ Device Identity Lookup ] (Query workstation by PcId in DB)
              ├───► Unknown Device ───► FAIL with 'DEVICE_NOT_REGISTERED'
              └───► Registered Device
                        │
                        ▼
            [ Status Authorization ]
              ├───► IsDisabled = true ───► FAIL with 'AUTH_FAILED'
              └───► Enabled Device
                        │
                        ▼
            [ Connection Binding & Registry ]
              ├───► Check Concurrent Connection Protection (Replace Policy)
              └───► Bind Connection, Set status to 'Online', Log CLIENT_CONNECTED
```

---

## 4. CONCURRENT CONNECTION PROTECTION

To prevent a workstation from getting associated with conflicting persistent connections (such as during physical reboot/rapid reconnect scenarios), the backend enforces a deterministic **Replace Existing Connection** policy:

1. During authentication, if another active connection with the same `PcId` is found in `ITcpConnectionRegistry`, the backend gracefully closes and unregisters the old connection first.
2. The new connection is then successfully bound and mapped in the registry.
3. This guarantees that at most one connection has control over a workstation's state at any point in time.

---

## 5. REDIS & PERSISTENT DATA ARCHITECTURE

- **PostgreSQL**: Remains the persistent, authoritative source of truth.
  - A unique constraint index is placed on `pc_id` and `mac_address`.
  - Regular indexes are created for `site_id`, `status`, and `last_seen`.
  - The unique constraint on `ip_address` has been removed since physical IP addresses are assigned dynamically via DHCP.
- **Redis Cache**: Ephemeral workstation state metadata (e.g. `Active` status, timestamps, hostname, client version) is cached securely in Redis under `v1:connection:{id}:state` with a 24-hour Time-To-Live (TTL). Plaintext session keys or secret keys are strictly banned from caching.
- **Graceful Cleanup**: Upon client disconnection, ungraceful socket loss, or server shutdown, the cached metadata is immediately removed from Redis, and the workstation transitions to `Offline` in PostgreSQL.

---

## 6. AUDIT & LIFE-CYCLE EVENT LOGGING

We have integrated security and operational audit trails into PostgreSQL:

- `CLIENT_REGISTERED`: Logged when a new workstation registers or updates its metadata via HTTP API.
- `CLIENT_CONNECTED`: Logged when a persistent TLS connection is successfully authenticated and bound.
- `CLIENT_DISCONNECTED`: Logged when an authenticated connection disconnects gracefully or ungracefully.
- `DEVICE_NOT_REGISTERED`: Logged when an unregistered workstation tries to authenticate.
- `DEVICE_AUTHORIZATION_FAILED`: Logged when a disabled device tries to authenticate.

---

## 7. SECURITY CONTROLS

- **No Secret Leakage**: Cryptographic session keys, master keys, and private keys are never exposed in log outputs.
- **No IP/Hostname Trust**: Handshake authentication depends strictly on the HMAC-SHA256 signature and `SAYRA_MASTER_KEY` verification. IP/Hostnames are treated solely as metadata.
- **Hardened Error Responses**: Structured JSON responses mapping RFC-compliant status codes (`403 Forbidden` for unregistered devices, `401 Unauthorized` for disabled ones) prevent any leakage of stack traces or database structures.

---

## 8. TEST VERIFICATION AND COVERAGE

A comprehensive, automated high-fidelity test suite has been developed and verified successfully:

- **Solution-Wide Test Metrics**:
  - **Total Tests Executed**: 69
  - **Passed**: 69
  - **Failed**: 0
  - **Skipped**: 0

### Test Categories and Count Details:

1. **Unit Tests (27 total)**:
   - Valid workstation creation, normalization, and validation rules.
   - Rejection and exception mapping of invalid PC IDs, Site IDs, hostnames, MAC addresses, and IP formats.
   - Robust state-machine status transitions (including invalid direct transitions).
   - Mocked registration command handling (idempotency, MAC uniqueness, and metadata updates).
2. **Integration Tests (42 total)**:
   - *Database Constraints*: Hardened Unique `PcId` index, MAC address uniqueness checks, and EF Core transaction rollbacks.
   - *Redis Cache*: Ephemeral connection state storage, lookup by key, and disconnect-cleanup policies.
   - *TCP Handshake & Binding*: Real persistent sockets over TLS 1.3 validating challenge-response, unknown device rejection, disabled device disconnection, concurrent connection replacement, and registry cleanup.

---
*Prepared by Jules, Principal Software Engineer.*
