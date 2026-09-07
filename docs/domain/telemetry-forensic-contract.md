# Telemetry Forensic & Compatibility Contract

This document provides an evidence-based forensic analysis of the telemetry, heartbeat, and operational event contract currently implemented between the SAYRA Client and Central Backend.

---

## A. Actual Message Inventory

| Message / Event Type | Producer | Transport | Envelope | Payload | Identity Binding | Timestamp Semantics | Sequence / ID | Retry / Queue | Backend Handler | Evidence Source |
|---|---|---|---|---|---|---|---|---|---|---|
| `HEARTBEAT` | Client | TLS 1.3 TCP Socket | `SecureMessageEnvelope` (HMAC-SHA256) | `HeartbeatMessage` (`pcId`, `timestamp`) | `connection.PcId` authenticated during handshake | `ClientTimestamp` (`Timestamp`) | None | Transient TCP retry | `TcpServer.ProcessSecureMessageAsync` line 438 | `src/Sayra.Backend.Contracts/HeartbeatContracts.cs`, `src/Sayra.Backend.Infrastructure/Transport/TcpServer.cs` |
| `PONG` | Server | TLS 1.3 TCP Socket | `SecureMessageEnvelope` (HMAC-SHA256) | `PongMessage` (`type="PONG"`, `timestamp`) | Bound to active socket session | `ServerTimestamp` (`DateTime.UtcNow`) | None | None (Server reply) | `TcpServer.ProcessSecureMessageAsync` line 476 | `src/Sayra.Backend.Contracts/HeartbeatContracts.cs`, `src/Sayra.Backend.Infrastructure/Transport/TcpServer.cs` |
| `TELEMETRY` | Client | TLS 1.3 TCP Socket | `SecureMessageEnvelope` (HMAC-SHA256) | `TelemetryModel` | `connection.PcId` verified against `Workstation` entity | `ClientTimestamp` (`Timestamp`) + `ServerReceivedAt` | None | Transient retry / connection drop | `IngestTelemetryCommandHandler` | `src/Sayra.Backend.Contracts/TelemetryContracts.cs`, `src/Sayra.Backend.Application/Telemetry/IngestTelemetryCommand.cs` |
| `CLIENT_EVENT` / `EVENT` | Client | TLS 1.3 TCP Socket | `SecureMessageEnvelope` (HMAC-SHA256) | `ClientEventEnvelopeDto` | `connection.PcId` matched against `ClientId` & `WorkstationId` | `ClientTimestamp` (`OccurredAt`) + `ServerReceivedAt` | `EventId` (Guid/String), `CorrelationId` | Redis dedup (`v1:event:dedup:{eventId}`, 24h TTL) | `IngestClientEventCommandHandler` | `src/Sayra.Backend.Contracts/ClientEventContracts.cs`, `src/Sayra.Backend.Application/Events/IngestClientEventCommand.cs` |
| `COMMAND_ACK` | Client | TLS 1.3 TCP Socket | `SecureMessageEnvelope` (HMAC-SHA256) | `commandId`, `status`, `failureReason` | `connection.PcId` | `ServerReceivedAt` | `commandId` | None | `IRemoteCommandManager.ProcessCommandAckAsync` | `src/Sayra.Backend.Infrastructure/Transport/TcpServer.cs` line 482 |
| `EXECUTION_RESULT` / `COMMAND_RESULT` | Client | TLS 1.3 TCP Socket | `SecureMessageEnvelope` (HMAC-SHA256) | `commandId`, `status`, `message`, `errorCode`, `result` | `connection.PcId` | `ServerReceivedAt` | `commandId` | None | `IRemoteCommandManager.ProcessCommandResultAsync` | `src/Sayra.Backend.Infrastructure/Transport/TcpServer.cs` line 538 |
| `SESSION_COMMAND_REQUEST` | Client | TLS 1.3 TCP Socket | `SecureMessageEnvelope` (HMAC-SHA256) | `SessionCommandPayload` (`action`, `gamerId`, `workstationId`, `sessionId`, `reservationId`) | `connection.PcId` RBAC validated via `UserPrincipal` | `ServerReceivedAt` | `idempotencyKey` | None | `TcpServer.ProcessSecureMessageAsync` line 565 | `src/Sayra.Backend.Infrastructure/Transport/TcpServer.cs` line 565 |

---

## B. Telemetry Schema

The `TelemetryModel` payload (`src/Sayra.Backend.Contracts/TelemetryContracts.cs`) contains the following properties:

| Field Name | CLR Type | Required? | Representation / Format | Unit | Semantics | Range | Timestamp Relationship | Owner | Counter / Gauge Semantics | Evidence Source |
|---|---|---|---|---|---|---|---|---|---|---|
| `Cpu` | `double` | Required | JSON Number | Percentage (`0–100%`) | System CPU utilization percentage | `0.0` to `100.0` | Measured at `Timestamp` | Client | Instantaneous Gauge | `src/Sayra.Backend.Application/Telemetry/IngestTelemetryCommand.cs` line 42 |
| `Ram` | `double` | Required | JSON Number | Megabytes (MB) | System RAM usage in MB | `0.0` to `double.MaxValue` | Measured at `Timestamp` | Client | Instantaneous Gauge | `src/Sayra.Backend.Application/Telemetry/IngestTelemetryCommand.cs` line 47 |
| `Uptime` | `double` | Required | JSON Number | Seconds | Workstation system uptime in seconds | `0.0` to `double.MaxValue` | Time since system boot | Client | Monotonic Cumulative Counter | `src/Sayra.Backend.Application/Telemetry/IngestTelemetryCommand.cs` line 52 |
| `Timestamp` | `DateTime` | Required | ISO 8601 UTC String | UTC Date/Time | Client sample collection timestamp | Valid past UTC DateTime | Client collection time | Client | Discrete Point-in-time | `src/Sayra.Backend.Contracts/TelemetryContracts.cs` line 10 |
| `RunningGameName` | `string?` | Optional | String | N/A | Name of currently running game process | Max 256 chars | Active process at sample time | Client | State Attribute | `src/Sayra.Backend.Application/Telemetry/IngestTelemetryCommand.cs` line 67 |
| `RunningGamePid` | `int?` | Optional | JSON Integer | PID | Process Identifier of active game | `> 0` | Process ID at sample time | Client | Ephemeral Identifier | `src/Sayra.Backend.Contracts/TelemetryContracts.cs` line 12 |
| `RunningGameCpu` | `double?` | Optional | JSON Number | Percentage (`0–100%`) | CPU utilization of active game process | `0.0` to `100.0` | Measured at `Timestamp` | Client | Instantaneous Gauge | `src/Sayra.Backend.Application/Telemetry/IngestTelemetryCommand.cs` line 57 |
| `RunningGameRam` | `double?` | Optional | JSON Number | Megabytes (MB) | RAM consumption of active game process | `0.0` to `double.MaxValue` | Measured at `Timestamp` | Client | Instantaneous Gauge | `src/Sayra.Backend.Application/Telemetry/IngestTelemetryCommand.cs` line 62 |
| `RunningGameDuration` | `double?` | Optional | JSON Number | Seconds | Elapsed duration of current game process execution | `0.0` to `double.MaxValue` | Process execution span | Client | Monotonic Process Counter | `src/Sayra.Backend.Contracts/TelemetryContracts.cs` line 15 |
| `TotalLaunches` | `int` | Required | JSON Integer | Count | Lifetime total game launches on workstation | `0` to `int.MaxValue` | Cumulative counter | Client | Monotonic Cumulative Counter | `src/Sayra.Backend.Contracts/TelemetryContracts.cs` line 16 |
| `TotalCrashes` | `int` | Required | JSON Integer | Count | Lifetime total process crashes on workstation | `0` to `int.MaxValue` | Cumulative counter | Client | Monotonic Cumulative Counter | `src/Sayra.Backend.Contracts/TelemetryContracts.cs` line 17 |
| `TotalRestarts` | `int` | Required | JSON Integer | Count | Lifetime total client application restarts | `0` to `int.MaxValue` | Cumulative counter | Client | Monotonic Cumulative Counter | `src/Sayra.Backend.Contracts/TelemetryContracts.cs` line 18 |

---

## C. Heartbeat Contract

- **Transport & Framing:** Transported over TLS 1.3 TCP framed socket via `SecureMessageEnvelope`.
- **Format:** Payload JSON object with `"type": "HEARTBEAT"`, `pcId`, `timestamp`.
- **Response:** Backend validates signature and immediately sends framed `PONG` response (`"type": "PONG"`, `timestamp`).
- **Connection Liveness vs. Telemetry Freshness:**
  - **Connection Liveness:** Managed by `TcpSessionManager` and `LivenessMonitoringWorker`. Updates `LastActivity` timestamp on session upon receiving `HEARTBEAT`.
  - **Telemetry Freshness:** Evaluated separately via `v1:telemetry:{PCID}:latest` in Redis and `TelemetryMetric` in PostgreSQL.
  - **Supported Architectural States:**
    1. `CONNECTED + TELEMETRY_FRESH` (TCP socket active, telemetry received within expected window)
    2. `CONNECTED + TELEMETRY_STALE` (TCP socket active/heartbeats passing, but telemetry frames paused or failing)
    3. `DISCONNECTED / OFFLINE` (TCP socket closed or liveness monitor timeout)

---

## D. Operational Event Contract

- **Envelope:** `ClientEventEnvelopeDto` (`EventId`, `EventType`, `ClientId`, `WorkstationId`, `SessionId`, `CorrelationId`, `OccurredAt`, `Payload`).
- **Verified Standard Event Types (`ClientEventType`):**
  - `CLIENT_STARTED`: Client application initial boot.
  - `CLIENT_STOPPED`: Graceful client shutdown.
  - `APPLICATION_STARTED`: Game/software process launch.
  - `APPLICATION_EXITED`: Normal process exit.
  - `APPLICATION_CRASHED`: Abnormal process termination.
  - `WORKSTATION_STATE_CHANGED`: Physical/logical workstation status change.
  - `CONFIGURATION_CHANGED`: Local config application notification.
  - `SECURITY_EVENT`: Local security violation or tampering attempt.
  - `NETWORK_CHANGED`: Local network interface state transition.
  - `DEVICE_CHANGED`: Peripheral/hardware attachment change.
  - `DIAGNOSTIC_EVENT`: Client diagnostic/log dump event.
  - `SESSION_RUNTIME_EVENT`: Billing session runtime event.
- **Deduplication:** Handler `IngestClientEventCommandHandler` computes Redis key `v1:event:dedup:{EventId}` with 24-hour TTL. Duplicate events return success idempotently.
- **Persistence:** Persisted as `AuditEvent` entity in PostgreSQL with `ServerReceivedAt` timestamp.

---

## E. Identity Model & Anti-Spoofing

- **Authoritative Identity:** Established during TCP authentication handshake (`ClientAuthenticationService`) and stored in `ITcpConnection.PcId`.
- **Payload Identity Verification:**
  - For `IngestClientEventCommand`: `ConnectionPcId` is checked against payload fields (`ClientId`, `WorkstationId`). If non-empty and mismatched, ingestion fails with error `"ClientId does not match authenticated connection PC-ID."`
  - For `IngestTelemetryCommand`: Ingestion uses `connection.PcId` authenticated on the active socket.
- **Rule:** Connection identity is authoritative. Workstation payload spoofing across TCP connections is rejected.

---

## F. Timestamp Model

1. `ClientTimestamp` (`t.Timestamp` / `evt.OccurredAt`): Generated by client at collection/occurrence time. Subject to client clock skew or manipulation.
2. `ServerReceivedAt` (`DateTime.UtcNow`): Authoritative server timestamp recorded by backend upon message arrival.
3. `ProcessedAt`: Timestamp recorded during database unit-of-work commit or downstream worker processing.
- **Clock-Skew Guidelines for Future Stages:**
  - Future ingestion pipelines (Stage 08-02+) must store both `ClientTimestamp` and `ServerReceivedAt`.
  - Stale samples with `ClientTimestamp` older than allowed window or in the future (> 5 mins ahead) must be flagged or rejected.

---

## G. Ordering, Idempotency & Sequence Numbers

- **Sequence Numbers:** The transport frame layer (`SecureMessageEnvelope`) supports optional `SequenceNumber`, managed per-session by `ISequenceValidator` (sliding window deduplication).
- **Event Idempotency:** `ClientEventEnvelopeDto.EventId` provides 24-hour deduplication via Redis `v1:event:dedup:{EventId}`.
- **State Overwrite Protection:** Out-of-order telemetry snapshots must not overwrite newer Redis cache entries (`v1:telemetry:{PCID}:latest`) if `ClientTimestamp` is older than existing entry.

---

## H. Offline and Reconnect Behavior

- Client queues events and telemetry locally during TCP disconnections.
- Upon reconnection and TLS/HMAC handshake completion, client replays queued messages over the secure channel.
- Duplicate event replays are safely deduplicated by backend `EventId` deduplication key in Redis.

---

## I. Security & Privacy Model

- **HMAC-SHA256 Signing:** Every TCP frame is enclosed in `SecureMessageEnvelope` and verified using session-derived cryptographic keys.
- **PII / Data Minimization:** Game telemetry transmits executable name (`RunningGameName`) and PID (`RunningGamePid`). No window titles, URLs, personal files, or keystrokes are collected or logged.
- **Audit Redaction:** `ISecurityEventService` and Serilog loggers redact passwords, tokens, and private keys.

---

## J. Cross-Phase Integration Map

- **Phase 03 (Session & Billing):** `SessionId` in client event envelope correlates directly with active billing sessions (`Session` entity). Workstation mapping links to `Workstation` entity.
- **Phase 05 (Real-Time Communication):** `ITcpSessionManager`, `ICommunicationSessionManager`, `ISecureMessageService`, and `TcpServer` transport layer handle encrypted framing and connection lifecycle.
- **Phase 06 (Configuration Management):** `TelemetryOptions` and future policy parameters configure ingestion limits and telemetry intervals.
- **Phase 07 (Software Updates):** `ClientVersionComparer` and update events correlate with package deployments and installation states.

---

## K. Discrepancy Classification

| Discrepancy / Area | Classification | Description & Resolution |
|---|---|---|
| `RunningGameName` length limit | COMPATIBLE | Enforced at 256 characters in `IngestTelemetryCommandHandler`. |
| CPU Percentage representation | COMPATIBLE | Enforced strictly in `0.0–100.0` range. |
| Memory representation | COMPATIBLE | Represented as Megabytes (MB) as `double`. |
| Client Event Deduplication | COMPATIBLE | Redis key `v1:event:dedup:{EventId}` provides 24h window. |
| Identity Binding Verification | COMPATIBLE | Mismatched payload `ClientId` / `WorkstationId` rejected against authenticated connection `PcId`. |

---

## L. Future PHASE 08 Dependency Map

1. **STAGE 08-02 (Telemetry Domain & Ingestion Architecture):** Builds telemetry ingestion pipeline, domain models, and Redis caching using the verified `TelemetryModel` and `ClientEventEnvelopeDto` schemas.
2. **STAGE 08-03 (Health Engine):** Consumes `Cpu`, `Ram`, `Uptime`, `TotalCrashes`, and `LastActivity` signals to compute `IWorkstationHealthEvaluator` health states (`HEALTHY`, `WARNING`, `DEGRADED`, `CRITICAL`, `UNKNOWN`, `OFFLINE`).
3. **STAGE 08-04 (Metrics & Observability):** Integrates OpenTelemetry `Meter("Sayra.Backend.Telemetry")` and Prometheus counters (`sayra_telemetry_received_total`, `sayra_telemetry_rejected_total`).
4. **STAGE 08-05 (Alerting Engine):** Monitors thresholds for sustained CPU/RAM pressure, telemetry absence, game crashes, and security events.
