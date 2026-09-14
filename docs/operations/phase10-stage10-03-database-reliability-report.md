# Phase 10 — Stage 10-03: Database Reliability & Data Integrity Report

## Executive Summary
This document provides the canonical evidence report for **Phase 10 Stage 10-03: Database Reliability & Data Integrity** of the SAYRA Central Backend.

Building upon the Stage 10-01 Production Readiness Baseline and Stage 10-02 Application Resilience Framework, Stage 10-03 hardens the PostgreSQL + EF Core + transaction + concurrency + schema integrity layer. This ensures that database operations remain bounded, atomic where required, concurrency-safe, recoverable where safe, and incapable of silently producing invalid business state under production load, transient network failure, concurrent requests, or node crashes.

---

## 1. Database Reliability Baseline
- **Provider & Version**: Npgsql.EntityFrameworkCore.PostgreSQL targeting PostgreSQL v15+.
- **DbContext Topology**: `ApplicationDbContext` is registered as `Scoped`, ensuring single-thread request/operation isolation. Service scopes created via `IServiceScopeFactory` isolate background worker iterations (`LivenessMonitoringWorker`, `RemoteCommandTimeoutWorker`, `TelemetryAggregationWorker`, `WorkstationHealthEvaluationWorker`) and async TCP/TLS session operations (`TcpServer`, `SecureMessageService`, `ClientAuthenticationService`).
- **Connection Configuration**:
  - `DatabaseOptions:ConnectionString` (enforced non-empty on startup via `ConfigurationValidator`).
  - `MaxPoolSize`: 100 (default, configurable via `DatabaseOptions:MaxPoolSize`).
  - `CommandTimeout`: 15 seconds (configurable via `DatabaseOptions:CommandTimeout`).
  - `ConnectionTimeout`: 15 seconds (configurable via `DatabaseOptions:ConnectionTimeout`).
  - `EnableRetryOnFailure`: 3 transient retries configured on Npgsql options.

---

## 2. Connection Pool & DbContext Audit
- **Connection Pool Sizing**:
  - Expected concurrent connection demand: 100 Max Pool Size accommodates ~50 concurrent HTTP API threads + 30 persistent TCP session workers + 4 background periodic workers + 16 buffer margin.
  - Connection consumption is strictly bounded because DbContexts are held only for the duration of unit-of-work scope and disposed immediately via scope termination (`using var scope = _scopeFactory.CreateScope()`).
- **DbContext Lifetime Audit**:
  - Scoped lifetime verified across all controllers and MediatR/CQRS handlers.
  - Zero instances of DbContext shared across concurrent threads or tasks.
  - Background workers create transient scopes per timer tick iteration, avoiding tracked entity leak or long-lived DbContext state degradation.

---

## 3. Critical Query Audit
- **Tracking Behavior**: Read-only queries across repository implementations (`UpdateReleaseRepository`, `UpdatePackageRepository`, `UpdateTargetRepository`, `DeadLetterEventRepository`, `TelemetryHistoryRepository`, `TelemetryAggregateRepository`, `IncidentRepository`, `WorkstationStreamStateRepository`) explicitly invoke `.AsNoTracking()` to avoid EF Core change tracking overhead.
- **N+1 Avoidance**: Complex aggregate queries (e.g. Session timing with rate snapshots, Workstation assignments, Update releases with packages) project directly to contracts or use eager loading (`Include`/`ThenInclude`) rather than lazy loading loops.
- **Query Cancellation**: All EF Core repository query methods accept and pass `CancellationToken` down to async EF Core methods (`FirstOrDefaultAsync`, `ToListAsync`, `CountAsync`, `ExecuteAsync`).
- **Bounded Result Sets**: Pagination and maximum row capping are enforced across all query endpoints (e.g., historical telemetry query limit capped at 10,000, incident history capped at 1,000, audit event logs capped at 1,000).

---

## 4. Transaction Boundary Matrix

| Operation Scope | Read / Write Set | Isolation Level | Transaction Boundary | Failure & Recovery Semantics |
|---|---|---|---|---|
| **Session Start / Stop / Extend** | `sessions`, `workstations`, `ratesnapshots`, `gamer_accounts` | Read Committed / Execution Strategy | Atomic `IDbContextTransaction` via `ExecuteInTransactionAsync` | Automatic Rollback on failure. Idempotency key protection prevents duplicate billing on retry. |
| **Financial Credit / Debit / Payment** | `gamer_accounts`, `ledger_entries`, `financial_transactions`, `payments` | Read Committed / Execution Strategy | Atomic `IDbContextTransaction` with balance assertions | Automatic Rollback on failure. `IdempotencyKey` unique index prevents double deposit/charge. `NonRetryableWrite` prevents automatic retry on uncertain status. |
| **Reservation Creation / Cancellation** | `reservations`, `workstations` | Read Committed | Atomic `IDbContextTransaction` | Overlapping reservation check enforced by state predicate and transaction scope. |
| **Configuration Publication & Rollback** | `configuration_packages`, `configuration_publications`, `configuration_assignments` | Read Committed | Atomic `IDbContextTransaction` | Active publication supersession is atomic. Rollback creates fresh immutable version without mutating historical packages. |
| **Update Release Activation & Rollback** | `update_releases`, `update_packages`, `update_targets` | Read Committed | Atomic `IDbContextTransaction` | Re-activates known-good historical release and supersedes active release atomically. |
| **Offline Event Reconciliation** | `processed_events`, `dead_letter_events`, domain aggregates | Read Committed | Atomic per event/batch transaction | Duplicates detected via `ProcessedEvent` unique index (`EventId`). Hash mismatches routed to DLQ. |

---

## 5. Concurrency & State Transition Matrix

| Aggregate / Entity | Concurrency Mechanism | Token / Attribute | Conflict Behavior | Protection Goal |
|---|---|---|---|---|
| **`UserCredential`** | Optimistic Concurrency | `uint RowVersion` | `DbUpdateConcurrencyException` | Prevents concurrent password/hash updates from overwriting security state. |
| **`GamerCredential`** | Optimistic Concurrency | `uint RowVersion` | `DbUpdateConcurrencyException` | Prevents concurrent gamer credential modifications. |
| **`ConfigurationPackage`** | Optimistic Concurrency | `uint RowVersion` | `DbUpdateConcurrencyException` | Guarantees immutability of published configuration content. |
| **`ConfigurationPublication`**| Optimistic Concurrency | `uint RowVersion` | `DbUpdateConcurrencyException` | Prevents race condition during concurrent configuration activations. |
| **`UpdateRelease`** | Optimistic Concurrency | `uint RowVersion` | `DbUpdateConcurrencyException` | Prevents concurrent update release state transitions. |
| **`UpdateTarget`** | Optimistic Concurrency | `uint RowVersion` | `DbUpdateConcurrencyException` | Prevents concurrent rollout percentage updates. |
| **`Incident`** | Optimistic Concurrency | `uint RowVersion` | `DbUpdateConcurrencyException` | Prevents alert storm race conditions on incident lifecycle updates. |
| **`Session`** | State Machine Predicate | Domain State Validation | Invalid state error (HTTP 400/409) | Blocks illegal transitions (e.g. `Ended` -> `Active`). |
| **`Workstation`** | Atomic Unique Index | `PcId` / `MacAddress` Unique Index | Unique constraint violation (HTTP 409) | Guarantees single physical device binding. |

---

## 6. Database Integrity Constraint Audit
- **Foreign Keys**: Enforced across all relational tables (`Site -> Organization`, `Zone -> Site`, `Workstation -> Zone/Site/Organization`, `Session -> Workstation/Gamer`, `LedgerEntry -> GamerAccount`, `PricingRule -> PricingPlan`, `ConfigurationAssignment -> ConfigurationPackage/Target`, `UpdatePackage -> UpdateRelease`). Cascades set to `Restrict` on critical aggregates to prevent accidental cascade deletion of financial and audit records.
- **Unique Constraints**:
  - `users`: `Username` (unique, lowercase)
  - `workstations`: `PcId` (unique), `MacAddress` (unique)
  - `organizations`: `Code` (unique)
  - `sites`: `(OrganizationId, Code)` (unique)
  - `roles`: `Name` (unique)
  - `permissions`: `Code` (unique)
  - `financial_transactions`: `IdempotencyKey` (unique)
  - `payments`: `IdempotencyKey` (unique)
  - `processed_events`: `EventId` (unique)
  - `dead_letter_events`: `EventId` (unique)
  - `configuration_packages`: `(Name, VersionNumber)` (unique)
  - `update_releases`: `(OrganizationId, Version)` (unique)
  - `update_packages`: `StorageKey` (unique)
  - `telemetry_aggregate_records`: `(WorkstationId, Granularity, WindowStart)` (unique)
- **Nullability**: All identifier, monetary amount, status, timestamp, and cryptographic signature columns marked `NOT NULL`.
- **Decimal Precision**: Monetary values (`Balance`, `Amount`, `HourlyRate`, `MinuteRate`, `TotalPrice`) configured explicitly with `decimal(18,4)` precision across EF Core entity configurations.
- **Timestamp Semantics**: All entity timestamp properties (`CreatedAt`, `UpdatedAt`, `ServerReceivedAt`, `ProcessedAt`, `WindowStart`, `WindowEnd`, `FirstTriggeredAtUtc`, `LastObservedAtUtc`) enforced as UTC (`timestamptz` / `DateTime.UtcNow`).

---

## 7. Idempotency Database Anchor Audit
- **Financial Transactions**: `FinancialTransaction` and `Payment` tables feature unique index constraints on `IdempotencyKey`. Duplicate API calls with the same idempotency key are safely intercepted by PostgreSQL, returning the existing transaction payload without re-applying debit/credit logic.
- **Offline Batch Events**: `ProcessedEvent` table features unique index on `EventId`. `OfflineOrderingAndReconciliationEngine` attempts insertion; duplicate `EventId` collisions return `AlreadyProcessed` without re-executing domain side effects.
- **Telemetry Event Ingestion**: Inbound operational events check `TelemetryIdempotencyService` (Redis) with fall-through unique index protection on persistent `AuditEvent` records.

---

## 8. Migration & Schema Safety Audit
- **Migration History Review**: All EF Core migrations from Phase 01 through Phase 08 reviewed:
  1. `20260809071653_InitialCreate`
  2. `20260810113847_AddWorkstationIdentityFields`
  3. `20260815073057_AddWorkstationProvisioningFields`
  4. `20260815091141_AddOrganizationSiteZoneHierarchy`
  5. `20260815100921_AddGamerAccountCredentialDomain`
  6. `20260816072203_AddReservationDomain`
  7. `20260816074405_AddSessionDomain`
  8. `20260816080048_AddSessionSegments`
  9. `20260816083724_AddPricingTariffEngine`
  10. `20260817090129_AddFinancialAccountAndLedger`
  11. `20260818104358_AddSessionExtensionAndIntegration`
  12. `20260820095342_AddUserIdentityAndCredentialFoundation`
  13. `20260822053602_AddUserCredentialRowVersion`
  14. `20260822072743_AddUserOrgAndSiteScope`
  15. `20260822073937_AddRbacUniqueIndexes`
  16. `20260823052418_AddAuthenticationSession`
  17. `20260823055228_AddRbacStatusAndExtensions`
  18. `20260823195714_AddSecurityEventsAndLoginProtection`
  19. `20260824100000_UpdateConfigurationPackageVersioning`
  20. `20260825000000_AddConfigurationSigningMetadataAndKeyRegistry`
  21. `20260826000000_AddConfigurationPublicationAndLifecycle`
  22. `20260904120000_AddUpdateDomainFoundation`
  23. `20260905100000_AddUpdateTargetingAndRollout`
  24. `20260906000000_AddTelemetryHistoryAndHeartbeatStorage`
  25. `20260907000000_AddTelemetryAggregationAndCheckpoints`
  26. `20260908000000_AddAlertingAndIncidentState`
- **Safety Determination**: All existing migrations preserve backward compatibility, avoid destructive column drops, enforce explicit non-null defaults, and include proper indexing. Migration history remains intact and immutable.

---

## 9. Schema Drift Report
- **Model vs Migration Analysis**: EF Core `ApplicationDbContextModelSnapshot.cs` fully matches current domain aggregate annotations and entity configuration files in `src/Sayra.Backend.Infrastructure/Persistence/Configurations/`.
- **Drift Status**: ZERO schema drift detected. Database schema, unique indexes, optimistic concurrency tokens, and EF Core entity mappings are 100% aligned.

---

## 10. Database Failure Recovery Matrix

| Failure Scenario | Immediate Impact | Application Reaction | Recovery Mechanism | Data Risk |
|---|---|---|---|---|
| **PostgreSQL Connection Drops (Transient)** | Database query throws `NpgsqlException` | Npgsql transient retry policy executes up to 3 retries. Application `ResiliencePipeline` handles retry/circuit breaker. | Auto-reconnect via Npgsql pool. | None (Uncommitted transactions rollback). |
| **PostgreSQL Outage (Sustained > 15s)** | DB commands time out (15s limit) | Circuit breaker opens; API endpoints return HTTP 503 / 500; Redis cache continues serving read-only configuration. | System recovers automatically once PostgreSQL container recovers. | None. |
| **Deadlock during Concurrent Financial Debit** | Npgsql throws deadlock exception (SQLState 40001) | Handled by transient retry policy if safe; financial non-retryable write fails safely with structured error. | Retry or client error response. | None. |
| **Optimistic Concurrency Conflict** | Update fails with `DbUpdateConcurrencyException` | Handler catches exception, rejects stale mutation, and surfaces HTTP 409 Conflict. | Client re-fetches latest version and retries. | Stale overwrite prevented. |
| **Commit-Then-Crash (ACK Loss)** | DB transaction commits, server crashes before HTTP ACK sent | Client retries request with same `IdempotencyKey`. | DB unique index catches duplicate key and returns original result idempotently. | Duplicate business effect prevented. |

---

## 11. Integrity Validation Queries/Tests
- **Impossible State Validation Checks**:
  1. Active session pointing to non-existent workstation (`sessions.workstation_id NOT IN (SELECT id FROM workstations)`).
  2. Gamer account balance mismatch against ledger sum (`SELECT account_id FROM gamer_accounts WHERE balance != (SELECT SUM(amount) FROM ledger_entries WHERE account_id = gamer_accounts.id)`).
  3. Duplicate active configuration assignment per target (`SELECT target_id FROM configuration_assignments WHERE is_active = true GROUP BY target_id HAVING COUNT(*) > 1`).
  4. Multiple active update releases per organization (`SELECT organization_id FROM update_releases WHERE status = 'Active' GROUP BY organization_id HAVING COUNT(*) > 1`).
  5. Negative financial account balances where prohibited (`SELECT id FROM gamer_accounts WHERE balance < 0`).
- Dedicated validation tests implemented in `tests/Sayra.Backend.UnitTests/Persistence/DatabaseReliabilityAndIntegrityTests.cs`.

---

## 12. Stage 10-03 Test Evidence

### Test Execution Summary
- **Unit & Persistence Verification (`Sayra.Backend.UnitTests.csproj`)**:
  - All unit tests pass cleanly.
  - Dedicated test suite `DatabaseReliabilityAndIntegrityTests.cs` verifies:
    - `Optimistic_Concurrency_Conflict_Should_Throw_DbUpdateConcurrencyException`
    - `Financial_Transaction_Idempotency_Anchor_Prevents_Duplicate_Key_Insertion`
    - `Transaction_Rollback_On_Partial_Failure_Leaves_No_Orphan_Records`
    - `Query_Cancellation_Token_Propagation_Interrupts_Execution`
    - `Database_Integrity_Validation_Queries_Detect_Impossible_Business_States`
- **Architecture Invariants (`Sayra.Backend.ArchitectureTests.csproj`)**:
  - All 3 architecture tests pass cleanly.

---

## 13. Stage 10-03 Findings and Deferred Work

| Finding / Item | Description | Disposition | Target Stage |
|---|---|---|---|
| **Database Connection Pool & Command Timeout Hardening** | Added `CommandTimeout` and `ConnectionTimeout` settings to `DatabaseOptions` and Npgsql configuration. | **RESOLVED** | Stage 10-03 |
| **DbContext Lifetime & Scoping Audit** | Scoped DbContext usage verified across all services, controllers, and workers. | **RESOLVED** | Stage 10-03 |
| **Optimistic Concurrency & Idempotency Anchors** | `RowVersion` and unique indexes verified across all aggregate roots and financial/reconciliation paths. | **RESOLVED** | Stage 10-03 |
| **TCP Connection Admission Control & Reconnect Storms** | TCP connection rate limiting and backpressure during socket storms. | **DEFERRED** | Stage 10-04 |
| **Worker Supervision & Checkpoint Framework** | Complete worker lifecycle supervision and error recovery. | **DEFERRED** | Stage 10-05 |
| **Container Graceful Shutdown & Socket Draining** | SIGTERM handling and socket draining during deployment. | **DEFERRED** | Stage 10-06 |
| **Database Backup, PITR & Disaster Recovery** | PostgreSQL backup automation and point-in-time recovery testing. | **DEFERRED** | Stage 10-07 |
| **Database Capacity & Benchmark Certification** | Sustained load testing against 5,000 workstations. | **DEFERRED** | Stage 10-08 |

---

## Final Stage Gate Determination

```text
STAGE: 10-03
STATUS: READY FOR 10-04

Database: PostgreSQL v15+ (Npgsql / EF Core 8)
Connection Pool: MaxPoolSize 100, CommandTimeout 15s, ConnectionTimeout 15s configured in DatabaseOptions
DbContext: Scoped lifetime verified; zero cross-thread context sharing; worker background scopes isolated
Queries: AsNoTracking applied; pagination safety limits enforced; CancellationToken propagated
Transactions: Explicit IDbContextTransaction boundaries; balance check assertions; Rollback on partial failure
Concurrency: RowVersion optimistic concurrency on all critical aggregate roots; state machine transition validation
Constraints: FKs, NOT NULL, decimal(18,4) precision, UTC timestamps, and unique indexes verified
Idempotency: Database-anchored unique indexes on IdempotencyKey (Payments, Transactions) and EventId (ProcessedEvents)
Migrations: 26 migrations audited; history intact; zero schema drift detected
Schema Drift: ZERO drift detected between ApplicationDbContextModelSnapshot and entity configurations
Integrity Checks: Impossible state validation queries verified
Tests: All unit, architecture, and persistence integrity tests passing cleanly
Performance Evidence: Query cancellation and connection pool bounds verified
Observability: Database resilience metrics and health checks operational
Resolved Findings: Database pool options, command timeouts, DbContext lifetime, transaction boundaries, concurrency tokens, integrity checks
Deferred Findings: TCP admission control (10-04), Worker supervision (10-05), Deployment orchestration (10-06), Backup/Restore (10-07), Capacity benchmark (10-08)
Critical Risks: None remaining for 10-03
Blocking Issues: None
```
