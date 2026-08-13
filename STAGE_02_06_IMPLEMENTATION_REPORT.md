# STAGE 02-06 — Device Communication Integration & Validation

## 1. Executive Summary

This report documents the implementation and validation of **Stage 02-06: Device Communication Integration & Validation** for the SAYRA Central Backend. The objective of this stage is to establish full end-to-end compatibility between the Central Backend services and the SAYRA Client contract.

The implementation successfully connects and integrates UDP Discovery, TLS 1.3 TCP server communication, secure challenge-response handshakes, encrypted secure envelope messaging, Redis-based active session caching, and workstation synchronization.

---

## 2. Implemented Components & Workflows

### 2.1. UDP Discovery Workflow
*   **Packet Reception**: `UdpDiscoveryServer` binds to the configured UDP discovery port (default `37020`) and listens for incoming datagrams asynchronously. Malformed packets (including non-JSON datagrams or packets with missing fields) are captured, logged, and discarded gracefully without terminating the server.
*   **Packet Handling**: It inspects the `type` property of incoming requests. Upon detecting a `"DISCOVER_SAYRA_SERVER"` packet, it resolves the local outbound IP address relative to the sender's network path and formats a standard seconds-level ISO8601 timestamp.
*   **Signature Generation**: Calculates the cryptographically secure HMAC-SHA256 signature using the `SAYRA_MASTER_KEY` under the formula:
    `HMAC-SHA256(MasterKey, serverId + "|" + ip + "|" + tcpPort + "|" + timestamp)`
*   **Response Dispatch**: Serializes and sends a compliant `SAYRA_SERVER_RESPONSE` back to the sender's remote endpoint.

### 2.2. Workstation Synchronization & Handshake Validation
*   **Handshake flow**: Coordinates the secure challenge-response workflow:
    1.  Backend receives TCP connection and emits `AUTH_CHALLENGE` with a secure random challenge.
    2.  Client returns `AUTH_RESPONSE` with HMAC response, encrypted session key, PC-ID, and client environment details.
    3.  Backend decrypts the SessionKey, validates the client's signature, and performs device authorization checks.
    4.  Backend returns `AUTH_STATUS` (`SUCCESS` or `FAILED`).
*   **Unregistered Device Protection**: No silent on-the-fly registration side effects are performed during authentication. If a workstation is unregistered or unrecognized, it is safely rejected with `DEVICE_NOT_REGISTERED` and disconnected.
*   **Workstation Synchronization**: Upon successful authentication, the registered workstation's database record is updated in a transaction with:
    -   `LastSeen` (timestamp updated to `DateTime.UtcNow`)
    -   `IpAddress` (resolved from connection socket)
    -   `ClientVersion` (synced from client response)
    -   `Hostname` and `SiteId` (synced from client response)
    -   `Status` transitioned to `"Online"`.

### 2.3. Redis Connection Session Caching
*   **Active States**: When a connection is authenticated, a session state is cached in Redis with an isolated key under the schema: `v1:connection:{connectionId}:state`.
*   **Metadata**: Stores connection metadata: `ConnectionId`, `PcId`, `State`, `ConnectedAt`, and `LastActivity`.
*   **TTL**: Created with a 24-hour TTL.
*   **Disconnection Cleanup**: Automated cleanups remove the Redis session state immediately when clients disconnect gracefully or abruptly.

### 2.4. Heartbeat & Secure Message Envelope Routing
*   **Decryption & Routing**: Decrypts and validates all incoming `SecureMessageEnvelope` payloads.
*   **Heartbeat Handling**: Upon receiving a `"HEARTBEAT"` message:
    -   Updates the connection's `LastActivity` locally.
    -   Updates the workstation's `LastSeen` and status in PostgreSQL.
    -   Renews and updates the connection session's `LastActivity` in Redis, refreshing the 24-hour TTL.
    -   Encrypted, signed, and returns a secure `"PONG"` message envelope back to the client.

---

## 3. Security Validation & Resilience Results

*   **Credential Leakage Prevention**: Never logs `SAYRA_MASTER_KEY`, SessionKeys, or decrypted payloads containing secrets/passwords. All cryptographic keys are protected inside private variables.
*   **Replay & Clock Drift Protection**: Enforces a strict $\pm300$-second window drift check on the `SecureMessageEnvelope` timestamp. Exceeded drifts or invalid signatures result in safe, immediate connection termination.
*   **Malformed Packets Robustness**: Malformed JSON or oversized TCP frames (exceeding limits) are rejected at the stream parser level.
*   **Database Constraints**: Uniqueness index constraints for `MacAddress` and `PcId` are preserved and enforced in DbContext to prevent workstation identifier collisions.

---

## 4. Test Verification Summary

The complete suite of **104 tests** (59 Unit Tests, 42 Integration Tests, 3 Architecture Tests) ran and passed with **100% success**:

```
Test run for /app/tests/Sayra.Backend.ArchitectureTests/bin/Debug/net8.0/Sayra.Backend.ArchitectureTests.dll (.NETCoreApp,Version=v8.0)
Passed!  - Failed:     0, Passed:     3, Skipped:     0, Total:     3, Duration: 17 ms - Sayra.Backend.ArchitectureTests.dll (net8.0)

Test run for /app/tests/Sayra.Backend.UnitTests/bin/Debug/net8.0/Sayra.Backend.UnitTests.dll (.NETCoreApp,Version=v8.0)
Passed!  - Failed:     0, Passed:    59, Skipped:     0, Total:    59, Duration: 331 ms - Sayra.Backend.UnitTests.dll (net8.0)

Test run for /app/tests/Sayra.Backend.IntegrationTests/bin/Debug/net8.0/Sayra.Backend.IntegrationTests.dll (.NETCoreApp,Version=v8.0)
Passed!  - Failed:     0, Passed:    42, Skipped:     0, Total:    42, Duration: 8 s - Sayra.Backend.IntegrationTests.dll (net8.0)
```

### Key Added/Extended Scenarios:
1.  `UdpDiscovery_ValidRequest_Should_Return_ValidSignatureResponse` — Validates UDP request-response, IP routing lookup, and mathematically verifies the HMAC-SHA256 signature.
2.  `UdpDiscovery_MalformedRequest_Should_Be_Safely_Ignored` — Asserts that malformed packets do not crash the discovery server and that the listener remains responsive.
3.  `PostAuth_Heartbeat_Should_Receive_Pong_And_Update_LastActivity_And_Redis` — Emulates a complete secure lifecycle: handshake -> connection state active -> secure HEARTBEAT envelope sent -> secure PONG envelope received -> database updated -> Redis session renewed.

---

## 5. Compatibility Review

A detailed comparison against the **SAYRA Central Backend Phase 02** specification confirms full compliance:
*   No changes were made to the Client side.
*   The communication contracts, properties, signature algorithms, and JSON structures are exactly aligned with the client-facing specification.
*   There are no remaining incompatibilities or ambiguities. The implementation is verified as production-grade and fully validated.
