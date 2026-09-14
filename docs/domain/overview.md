# Domain Model & Business Rules

This document details the core domain aggregates, business rules, financial invariants, session state machines, and configuration control plane for the **SAYRA Central Backend**.

---

## 1. Domain Aggregate Overview

The domain layer (`Sayra.Backend.Domain`) contains pure domain logic, entities, value objects, and domain events without external framework dependencies.

### Key Domain Aggregates & Entities
1. **User / Gamer (`User`, `GamerProfile`)**: Models system identities (`Gamer`, `Operator`, `Manager`, `Administrator`) with account state transitions (`Pending`, `Active`, `Suspended`, `Locked`, `Disabled`, `Deleted`) and failed login lockout protection.
2. **Workstation (`Workstation`)**: Represents physical gaming PCs bound by unique `PcId` (Guid) and MAC address (`MAC-48` format validated via compiled regex). Tracks state (`Offline`, `Available`, `Occupied`, `Maintenance`, `Disabled`).
3. **WorkstationSession (`WorkstationSession`)**: Server-authoritative gaming session aggregate tracking start/end times, pre-paid or post-paid billing modes, rate snapshots, and consumed usage costs.
4. **GamerAccount (`GamerAccount`) & FinancialTransaction (`FinancialTransaction`)**: Financial double-entry ledger maintaining account balance, deposit history, usage debits, and compensation reversals.
5. **ConfigurationPackage (`ConfigurationPackage`) & ConfigurationTarget (`ConfigurationTarget`)**: Configuration control plane managing JSON schema validation, deterministic canonical normalization, versioning, JSON patch delta generation, RSA-SHA256 signatures, targeting, and publications.
6. **RemoteCommand (`RemoteCommand`)**: Command delivery aggregate root enforcing state machine transitions (`CREATED` ➔ `QUEUED` ➔ `SENDING` ➔ `DELIVERED` / `ACKNOWLEDGED` ➔ `EXECUTING` ➔ `SUCCEEDED` / `FAILED` / `EXPIRED`).
7. **UpdateRelease (`UpdateRelease`), UpdatePackage (`UpdatePackage`) & UpdateTarget (`UpdateTarget`)**: Software distribution platform managing update release state transitions (`Draft` ➔ `Validated` ➔ `Ready` ➔ `Published` ➔ `Active` / `Superseded` / `Revoked`), zip/spk container validation, SHA-256 integrity, RSA-SHA256 digital signatures, multi-tier targeting, and staged rollout bucketing.

---

## 2. Core Business Invariants & Rules

### 2.1. Server Time Authority
* **Rule**: All timestamps, duration calculations, rate resolutions, and session expirations MUST use the server's authoritative clock (`DateTime.UtcNow`). Client workstation local clocks are untrusted and ignored.

### 2.2. Exact Monetary Precision
* **Rule**: Floating-point types (`float`, `double`) are strictly prohibited for monetary values.
* **Standard**: All monetary values use `.NET` `decimal` mapped to PostgreSQL `NUMERIC(18, 4)`.

### 2.3. Financial Ledger & Idempotency
* **Rule**: Every credit, debit, or payment operation must provide a unique `IdempotencyKey`.
* **Idempotency Enforcement**:
  * PostgreSQL unique index (`IX_FinancialTransactions_IdempotencyKey`) prevents duplicate processing.
  * Re-submitting the exact same request fingerprint returns the previous successful result. Re-submitting the same key with different parameters raises `HTTP 409 Conflict`.
* **Post-Paid Usage Debt**:
  * Pre-paid session debits enforce strict non-negative balance checks (`INSUFFICIENT_BALANCE`).
  * Post-paid session termination debits (`USAGE_CHARGE`) permit negative balances to accurately record consumed usage debt.

### 2.4. Effective Configuration Resolution Hierarchy
* **Hierarchy Rule**: When resolving the effective configuration for an authenticated workstation, the backend evaluates applicable target assignments in strict precedence order:
  $$\text{Workstation Scope} > \text{Group Scope} > \text{Site Scope} > \text{Global Scope}$$
* **Conflict Resolution**:
  * **Same Target**: Selects the package with the highest `VersionNumber`.
  * **Multi-Group**: Sorts assigned groups deterministically by `Code` ascending, then `Id`.
  * **JSON Merging**: Deep recursive object merging, scalar replacement, explicit null overrides, and complete array replacements.

### 2.5. Update & Software Distribution Platform Rules
* **Release State Machine**:
  `Draft` ➔ `Validated` ➔ `Ready` ➔ `Published` ➔ `Active` (or `Superseded` / `Revoked`).
  Published and Active releases are strictly immutable.
* **Package Integrity & Digital Signing**: Every update artifact requires container magic byte validation, streaming SHA-256 calculation, and Base64 RSA-SHA256 digital signing before publication. TOCTOU checks verify storage hash equality prior to release state promotion.
* **Targeting Precedence & Staged Rollout**:
  Evaluates scope precedence ($\text{Workstation} > \text{Group} > \text{Site} > \text{Global}$).
  Applies deterministic rollout bucketing:
  $$\text{Bucket} = \text{SHA256}(\text{PcId} + \text{ReleaseId}) \pmod{100}$$
  Monotonic expansion guarantees that increasing rollout percentages never eject previously eligible client workstations.
* **Secure Resumable Streaming**:
  Download API supports HTTP `200 OK` (full download) and HTTP `206 Partial Content` / `416 Range Not Satisfiable` (Range requests). Content streaming uses bounded 64 KB memory buffers ($O(1)$ memory usage) with `ETag` checksums and sanitized filename headers.

### 2.6. Telemetry, Health Evaluation, and Alerting & Incident State Subsystem
* **Telemetry & Event Ingestion**: Ingests heartbeats, telemetry metrics, and operational events with strict identity binding (`connection.PcId` authoritative), deduplication, and historical persistence.
* **Workstation Health Evaluation**: Evaluates workstation metrics against `WorkstationHealthPolicyOptions` thresholds with hysteresis recovery, producing authoritative health results (`Healthy`, `Warning`, `Degraded`, `Critical`, `Offline`).
* **Alerting & Incident State**: Evaluates health results against `AlertRule` options using deterministic SHA-256 fingerprints ($\text{OrganizationId} \parallel \text{SiteId} \parallel \text{PcId} \parallel \text{RuleCode} \parallel \text{Resource}$). Manages durable incident lifecycle transitions (`Normal` ➔ `Triggered` ➔ `Firing` ➔ `Resolved`), observation counts, policy suppression, notification dispatch, and alert storm protection. See [Phase 08 E2E Validation Report](../operations/phase08-e2e-validation-report.md) for capstone acceptance evidence.

### 2.7. Application Resilience & Dependency Failure Handling Policy
* **Resilience Pipeline**: Governs PostgreSQL, Redis, and local storage interactions with bounded retries, exponential backoff with randomized jitter, per-attempt timeouts, overall logical operation deadlines, cancellation propagation, and `Sayra.Backend.Resilience` OpenTelemetry metrics.
* **Write Safety Enforcement**: Non-idempotent writes (financial debits/credits, payments) are strictly prohibited from automatic retry on uncertain execution status.
* **Circuit Breaker Protection**: Non-critical or external dependencies utilize thread-safe `CircuitBreaker` instances (`Closed` ➔ `Open` ➔ `HalfOpen`) to fast-reject calls during outages and prevent load amplification.
* **Redis Fail-Open Degradation**: Real-time caching and telemetry freshness checks degrade gracefully to PostgreSQL or fail open to avoid process crashes or telemetry ingestion blockages during Redis outages. See [Application Resilience Policy](../operations/app-resilience-policy.md) and [Phase 10 Stage 10-02 Report](../operations/phase10-stage10-02-application-resilience-report.md) for full details.

### 2.8. Database Reliability & Data Integrity Policy
* **Connection Pooling & DbContext Lifetime**: Bounded connection pooling (MaxPoolSize 100, CommandTimeout 15s) with scoped DbContext lifetimes isolating HTTP API requests, TCP session workers, and periodic background workers (`IServiceScopeFactory`).
* **Optimistic Concurrency & Transaction Boundaries**: `RowVersion` optimistic concurrency tokens across all critical aggregate roots (`UserCredential`, `GamerCredential`, `ConfigurationPackage`, `ConfigurationPublication`, `UpdateRelease`, `UpdateTarget`, `Incident`). Critical business operations enforce explicit `IDbContextTransaction` boundaries with atomic rollback on partial failure.
* **Database Idempotency Anchors & Schema Integrity**: Database-anchored unique index constraints enforce idempotency across financial transactions (`IX_FinancialTransactions_IdempotencyKey`), payments (`IX_Payments_IdempotencyKey`), and offline event reconciliation (`IX_ProcessedEvents_EventId`). Query cancellation tokens are propagated across all repository methods, with result set bounds enforced to prevent unbounded memory allocation. See [Phase 10 Stage 10-03 Report](../operations/phase10-stage10-03-database-reliability-report.md) for full details.
