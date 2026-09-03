# Technical Risk Matrix & Mitigations

This document outlines the primary technical risks identified for the **SAYRA Central Backend** and their corresponding architectural mitigations.

---

## Technical Risk Matrix

| Risk Code | Technical Risk Description | Severity | Architectural Mitigation Strategy |
|---|---|---|---|
| **R-01** | **Client Protocol Immutability Lock-In**: The compiled client binary cannot be modified. Any deviation in socket framing, TLS handshakes, HMAC signatures, or JSON property ordering causes total communication failure. | **CRITICAL** | Mirror client message specifications strictly in `Sayra.Backend.Contracts`. Enforce canonical deterministic JSON serialization (`CanonicalConfigurationSerializer` / `StringComparer.Ordinal`) and validate against integration test suites (`Phase03ApiAndContractTests`). |
| **R-02** | **Offline Local Root CA & PKI Lifecycle**: Managing a Local Root Certificate Authority without internet access to commercial CAs can lead to operational certificate expiration or client trust failures. | **HIGH** | The backend automatically provisions a self-managed Local Root CA on initial boot, generates server certificates, and configures configurable validity periods in `ServerOptions`. Workstations register the CA in local trust stores during bootstrapping. |
| **R-03** | **Socket Buffer Allocation & GC Pressure**: High-frequency telemetry (every 10-30s from hundreds of workstations) could trigger excessive Garbage Collection (GC) pauses and degrade API responsiveness. | **HIGH** | Utilize `System.IO.Pipelines` and `ArrayPool<byte>` in `TcpFrameParser` and `TcpConnection` for completely allocation-free buffer parsing. Enforce configurable maximum message frame size limits (`MaximumMessageSize`). |
| **R-04** | **Concurrent Financial Mutations & Race Conditions**: Simultaneous deposit, debit, or session termination requests on the same gamer account could cause financial drift or duplicate charging. | **CRITICAL** | Enforce database-level transactions (`ExecuteInTransactionAsync`), EF Core optimistic concurrency tokens (`uint RowVersion`), and unique PostgreSQL indexes on `IdempotencyKey`. Conflicting key re-use returns `HTTP 409 Conflict`. |
| **R-05** | **TCP Network Disconnects & Stale Session State**: Abrupt workstation power loss or network drops leave active TCP connections in a dangling state, blocking new reconnections. | **MEDIUM** | `LivenessMonitoringWorker` evaluates configurable heartbeat timeouts (`HeartbeatTimeout`, `HeartbeatGracePeriod`). Stale connections are terminated gracefully, releasing session tracking state in `TcpSessionManager` and `ISequenceValidator`. |
