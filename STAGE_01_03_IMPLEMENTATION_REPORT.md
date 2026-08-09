# STAGE 01-03 — PERSISTENCE & INFRASTRUCTURE IMPLEMENTATION REPORT

## 1. EXECUTIVE SUMMARY & PROGRESS REPORT

This report documents the implementation and hardening of the primary persistence and infrastructure layer for the **SAYRA Central Backend**. Built as a modular monolithic architecture following Clean Architecture rules, the system's database model and caching layer have been designed for production-level durability, high write volumes, financial correctness, security, and offline LAN event resiliency.

All implementations strictly respect the immutable SAYRA Client contract. Later-stage business operations are explicitly excluded, laying only the robust persistence and infrastructure foundation.

### High-Level Status:
- **Solution Build**: `VERIFIED` — Compiles cleanly with zero errors and zero warnings across all projects.
- **PostgreSQL Persistence & Migrations**: `VERIFIED` — Successfully generated, executed, and rolled back initial EF Core migration against empty local PostgreSQL.
- **Precision/Numeric Financial Models**: `VERIFIED` — No floating-point types exist for currency. Scaled precisely as `numeric(18,4)`.
- **Idempotency Protection**: `VERIFIED` — Unique db-level index on `EventId` successfully catches duplicate retransmissions under concurrent workloads.
- **Hardened Redis Ephemeral State**: `VERIFIED` — Key generator namespaces, serialization security, TTL, and graceful degradation implemented and tested.
- **Overall Test Suite**: **18 / 18 Tests Passed (100% success rate)**.

---

## 2. DATABASE ARCHITECTURE & ENTITY SCHEMA

Using **PostgreSQL 16** as the primary relational database, six core schema entities are modeled in `Sayra.Backend.Domain` and explicitly configured under `Sayra.Backend.Infrastructure/Persistence/Configurations/` via `IEntityTypeConfiguration<T>` classes.

### Final PostgreSQL Tables and Mappings:

| Entity Name | Target Table | Primary Key | Critical Indexes / Constraints | Type & Precision | Purpose / Behavior | Status |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| `Workstation` | `Workstations` | `Id` (UUID) | Unique IP, Unique MAC | `IpAddress` (varchar 50)<br>`MacAddress` (varchar 50)<br>`Status` (varchar 50) | Core workstation metadata & liveness state | `IMPLEMENTED`<br>`VERIFIED` |
| `WorkstationSession` | `WorkstationSessions` | `Id` (UUID) | Index on WorkstationId, GamerId, SessionState | `RatePerHour` (numeric 18,4)<br>`CurrentCost` (numeric 18,4)<br>`RemainingCredits` (numeric 18,4)<br>`BillingAmount` (numeric 18,4) | Tracks active/closed player sessions and financial bounds | `IMPLEMENTED`<br>`VERIFIED` |
| `AuditEvent` | `AuditEvents` | `Id` (UUID) | **Unique index on EventId**; Indices on WorkstationId, SessionId, Timestamp | `EventId` (UUID)<br>`Payload` (**jsonb**)<br>`Timestamp` (timestamptz) | Immutable audit trails, optimized for offline duplicate checks | `IMPLEMENTED`<br>`VERIFIED` |
| `TelemetryMetric` | `TelemetryMetrics` | `Id` (UUID) | **Composite index: (WorkstationId, Timestamp)**; Index on Timestamp | `MetricValue` (double)<br>`DimensionJson` (**jsonb**)<br>`Timestamp` (timestamptz) | Optimized for frequent high-volume time-range queries | `IMPLEMENTED`<br>`VERIFIED` |
| `ConfigurationPackage`| `ConfigurationPackages` | `Id` (UUID) | Unique index on Name + Version | `Content` (**jsonb**) | Structured config parameters, tracked with concurrency | `IMPLEMENTED`<br>`VERIFIED` |
| `SystemUpdate` | `SystemUpdates` | `Id` (UUID) | Unique index on Version | `ChecksumSha256` (varchar 64)<br>`DigitalSignature` (bytea) | Validates mandatory offline local updates securely | `IMPLEMENTED`<br>`VERIFIED` |

### Key Modeling Hardening Decisions:
1. **Financial Precision**: `RatePerHour`, `CurrentCost`, `RemainingCredits`, and `BillingAmount` are defined explicitly using `.HasPrecision(18, 4)` and map directly to `numeric(18,4)`. This prevents rounding errors and floating-point issues, laying the perfect foundation for future billing/accounting modules.
2. **UTC Timestamp Policy**: Every datetime property (`StartTime`, `EndTime`, `Timestamp`, `LastSeen`, `ReleaseDate`, `CreatedAt`, `UpdatedAt`) is registered as `timestamp with time zone` (PostgreSQL `timestamptz`), forcing absolute UTC persistence across server-client interactions.
3. **JSONB Mapping**: Properties representing structured documents (`AuditEvent.Payload`, `TelemetryMetric.DimensionJson`, `ConfigurationPackage.Content`) are explicitly registered with `.HasColumnType("jsonb")` to support high-performance JSON search, filtering, and indexing.
4. **Optimistic Concurrency**: Entities subject to concurrent edits (`Workstation`, `WorkstationSession`, `ConfigurationPackage`) incorporate a `uint RowVersion` property configured with `.IsRowVersion().IsConcurrencyToken()`. On PostgreSQL, this maps to the native system-managed `xmin` column to prevent concurrent update overwrites.
5. **EventId Idempotency**: To protect against duplicate offline retransmissions, a strict, unique database index (`IX_AuditEvents_EventId`) is configured on `AuditEvent.EventId`. Concurrent inserts of duplicate event records trigger a `DbUpdateException` (unique key violation), enforcing absolute data-level safety.

---

## 3. REPOSITORY, UNIT OF WORK, AND TRANSACTION MANAGEMENT

### Core Repository & Unit of Work Behaviors:
- **Tracking & No-Tracking choice**: `IRepository<T>` has been extended to support optional no-tracking queries (`bool track = true`) via `GetByIdAsync` and `GetAllAsync`. When `track` is false, EF Core operates with `.AsNoTracking()`, optimizing read-heavy queries.
- **Asynchronous Execution & Cancellations**: All database-level operations propagate a `CancellationToken` down to underlying EF Core drivers to handle query cancellation and timeout events.
- **Transaction Strategy**: The `IUnitOfWork` (implemented by `ApplicationDbContext`) exposes explicit transaction life-cycle methods:
  - `BeginTransactionAsync(cancellationToken)`
  - `CommitTransactionAsync(cancellationToken)` (internally calls `SaveChangesAsync` atomically and commits)
  - `RollbackTransactionAsync(cancellationToken)`
- **Disposal Safety**: Robust `Dispose()` and `DisposeAsync()` handlers on the DbContext guarantee that uncommitted transactions are cleanly rolled back and connections are gracefully returned to the PostgreSQL connection pool.

---

## 4. REDIS ARCHITECTURE & EPHEMERAL STATE

Redis is leveraged strictly for ephemeral, high-frequency, or distributed cluster states. Plaintext cryptographic secrets are strictly banned from Redis.

### Centralized Key Naming Utility:
To prevent scattered hardcoded keys, all Redis keys are defined through `RedisKeyGenerator.cs` using versioned, collision-resistant namespaces:
- **Workstation Online State**: `v1:workstation:{id:N}:state`
- **TCP Connection State**: `v1:connection:{id:N}:state`
- **Workstation Heartbeat State**: `v1:heartbeat:{id:N}:state`
- **Command Dispatch State**: `v1:command:{id:N}:state`
- **Idempotency/Replay State**: `v1:idempotency:{key}:state`

### Serialization & Security Policies:
- **Deterministic Serialization**: Object serialization/deserialization utilizes deterministic `JsonSerializer` schemas.
- **Logging Security**: An internal `MaskKey` function intercepts logging parameters inside `RedisService` to mask keys containing terms like `token`, `secret`, `key`, or `password` as `REDACTED_SENSITIVE_KEY`, completely preventing credential leaks in logging/observability diagnostics.
- **Error Resilience / Degradation**: Caught database or networking dropouts degrade gracefully, logging warnings instead of crashing the process, returning sensible defaults, and verifying operational liveness.

---

## 5. HEALTH CHECKS & API ENDPOINTS

Endpoint infrastructure is implemented in `HealthController.cs` and registers directly at `/api/health`:
- **`GET /api/health/live` (Liveness)**: `VERIFIED` — Process-only quick check. Does not depend on external databases. Operates independently during database restarts or short network dropouts.
- **`GET /api/health/ready` (Readiness)**: `VERIFIED` — Checks active connection parameters to PostgreSQL and Redis. Employs tags (`"ready"`) to isolate check dependencies.
- **Security Check**: Outputs exclude raw connection strings, usernames, passwords, or cluster details, protecting server internals.

---

## 6. VERIFICATION & TESTING RESULTS

A high-fidelity test suite has been established, running against live PostgreSQL and Redis services.

### Test Result Matrix:

| Category | Assembly Name | Passed | Failed | Total | Status | Key Coverage |
| :--- | :--- | :---: | :---: | :---: | :--- | :--- |
| **Unit Tests** | `Sayra.Backend.UnitTests` | 6 | 0 | 6 | `VERIFIED` | Precision Money records, Result wrapper, validation formatting rules |
| **Architecture Tests** | `Sayra.Backend.ArchitectureTests` | 3 | 0 | 3 | `VERIFIED` | Dependency boundaries, domain layer isolation, clean monolith module segregation |
| **Integration Tests** | `Sayra.Backend.IntegrationTests` | 9 | 0 | 9 | `VERIFIED` | Endpoint integrations, live PG transaction rollback, duplicate EventId violations, typed Redis storage/TTL |
| **Overall Metrics** | **SAYRA Server Test Suite** | **18** | **0** | **18** | `SUCCESS` | **100% Pass Rate** |

### Vital Architectural Test Implementations:
1. **Test Parallelization Decision**: Disabling parallel test execution (`DisableTestParallelization = true`) at the integration assembly level is crucial. Since multiple test classes start in-memory `WebApplicationFactory<Program>` instances, parallel startup tasks conflict with shared logging/resource parameters (such as Serilog bootstrap lock). Disabling parallelization guarantees clean sequential runs.
2. **EF ChangeTracker Isolation Fix**: In `Transaction_Should_Rollback_State_On_Failure`, EF Core's `FindAsync` first checks the local change tracker, returning tracked instances even if rolled back in PostgreSQL. Adding `dbContext.ChangeTracker.Clear()` before querying forces EF Core to query the database, validating transaction rollback.

---

## 7. SCOPE RESTRICTIONS (STAGE 01-03 BOUNDARIES)

In accordance with strict modularity rules, the following systems are **NOT** complete in Stage 01-03:
- **Billing / Financial accounting engine** — `NOT IN SCOPE` (only database structures and decimal bounds are defined).
- **Reservation engine** — `NOT IN SCOPE`
- **Gamer authentication business logic** — `NOT IN SCOPE`
- **Fleet management & Remote Command Execution** — `NOT IN SCOPE`
- **Complete UDP discovery service** — `NOT IN SCOPE`
- **Complete persisting TLS/TCP protocol communications** — `NOT IN SCOPE`
- **Complete update and configuration sync engine** — `NOT IN SCOPE`

---

## 8. KNOWN LIMITATIONS, DEBT & RISKS

1. **Local Database Fallback**: Development configurations default to a standard connection string when environment configurations are missing. Production configurations must configure `Database:ConnectionString` and `Redis:ConnectionString`.
2. **PostgreSQL-Specific Migrations**: Generating migrations using Npgsql locks migrations to PostgreSQL schemas. This is expected as PostgreSQL is the designated storage engine.
3. **Tracking Entity State in memory**: EF Core retains objects added during transactional failures in memory. Developers must call `ChangeTracker.Clear()` or dispose of the DbContext if a transaction fails.

---

## STAGE 01-03 COMPLETION ASSESSMENT

Based on the 100% successful implementation of PostgreSQL entities, precise financial configurations, EventId unique constraint violation protection, centralized versionable Redis key strategy, health checks, git hygiene cleanup, and a perfect **18/18** test pass rate:

**COMPLETE**

---
*Prepared by Jules, Principal Backend Architect.*
