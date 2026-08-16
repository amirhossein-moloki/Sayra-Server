# STAGE 03-04 IMPLEMENTATION REPORT — Session Domain Core & Lifecycle State Machine

**Project:** SAYRA Central Backend
**Stage:** STAGE 03-04 (Session Domain Core & Lifecycle State Machine)
**Author:** Senior Backend Engineer (Jules)
**Status:** Complete

---

## 1. Executive Summary

STAGE 03-04 delivers the core Session Domain aggregate and authoritative Session Lifecycle State Machine for the SAYRA Central Backend. The backend is now the single source of truth for session creation, lifecycle management, state transitions, and active workstation usage protection.

Out-of-scope features (Server Authoritative Timer calculations, Session Segments, Pricing Engine, Billing Engine, Financial Ledger, Payment Processing, and TCP transport integration) were strictly excluded, preserving clean domain boundaries for future Phase 03 stages.

---

## 2. Implemented Components & Files Changed

### Domain Layer (`Sayra.Backend.Domain`)
- **`Entities/Session.cs`**: Aggregate root representing gaming sessions on workstations with UTC timestamp normalization, state machine transition rules (`IDLE`, `STARTING`, `ACTIVE`, `PAUSED`, `ENDING`, `ENDED`, `EXPIRED`, `CANCELLED`, `TERMINATED`), active status helpers, and optimistic concurrency token (`RowVersion`).
- **`Events/SessionEvents.cs`**: Immutable domain event records:
  - `SessionCreated`
  - `SessionStarted`
  - `SessionPaused`
  - `SessionResumed`
  - `SessionStopped`
  - `SessionCancelled`
  - `SessionTerminated`

### Contracts Layer (`Sayra.Backend.Contracts`)
- **`SessionContracts.cs`**:
  - `StartSessionRequestDto`
  - `TerminateSessionRequestDto`
  - `SessionResponseDto`

### Application Layer (`Sayra.Backend.Application`)
- **`Sessions/ISessionStateTransitionService.cs`**: Abstraction for session validation and active session conflict detection.
- **`Sessions/SessionStateTransitionService.cs`**: Domain service checking Gamer eligibility (active/not disabled), Workstation eligibility (assigned, active, not deactivated/disabled), Workstation active session uniqueness, and Reservation linkage validity.
- **`Sessions/SessionCommandsAndQueries.cs`**:
  - Commands: `StartSessionCommand`, `PauseSessionCommand`, `ResumeSessionCommand`, `StopSessionCommand`, `CancelSessionCommand`, `TerminateSessionCommand`
  - Queries: `GetSessionQuery`, `GetActiveSessionByWorkstationQuery`, `GetActiveSessionByGamerQuery`
- **`Sessions/SessionValidators.cs`**: FluentValidation rules for commands and queries.
- **`Sessions/SessionHandlers.cs`**: Command and query handlers executing inside unit of work database transactions with audit event logging (`AuditEvent`).

### Infrastructure Layer (`Sayra.Backend.Infrastructure`)
- **`Persistence/Configurations/SessionConfiguration.cs`**: EF Core mapping for `sessions` table with foreign keys (`Gamer`, `Organization`, `Site`, `Workstation`, `Reservation`) using Restrict delete behavior, optimistic concurrency token (`RowVersion`), and multi-column status indexes.
- **`Persistence/ApplicationDbContext.cs`**: Registered `DbSet<Session> Sessions`.
- **`DependencyInjection.cs`**: Registered `ISessionStateTransitionService`, command handlers, and query handlers in the DI container.
- **`Migrations/20260816074402_AddSessionDomain.cs`**: EF Core database migration introducing the `sessions` table, foreign keys, and indexes.

### API Layer (`Sayra.Backend.Api`)
- **`Controllers/SessionsController.cs`**: REST API endpoints exposing `/api/sessions` for start, pause, resume, stop, cancel, terminate, get by ID, get active by workstation, and get active by gamer.

### Tests Layer (`tests/`)
- **`tests/Sayra.Backend.UnitTests/SessionUnitTests.cs`**: 10 new unit tests covering state machine transitions, invalid status transition rejection, timestamp normalization, and validation service rules.
- **`tests/Sayra.Backend.IntegrationTests/SessionIntegrationTests.cs`**: 5 new integration tests covering end-to-end REST lifecycle, PostgreSQL persistence, foreign keys, active workstation double-start conflict protection (409 Conflict), reservation state transitions, double-stop idempotency, and invalid transition handling.

---

## 3. Session Domain Rules & State Machine

### Status State Machine
Explicit state machine enforced by `Session.TransitionTo`:

```
   IDLE
    |
    v
 STARTING
    |
    v
  ACTIVE
  |    |
  |    +-------------------> PAUSED ---> ACTIVE
  |    |
  |    +-------------------> ENDING ---> ENDED
  |
  +------------------------> EXPIRED
  |
  +------------------------> CANCELLED
  |
  +------------------------> TERMINATED
```

- **Allowed Paths**:
  - `IDLE → STARTING → ACTIVE`
  - `ACTIVE → PAUSED → ACTIVE`
  - `ACTIVE → ENDING → ENDED`
  - `ACTIVE → EXPIRED / CANCELLED / TERMINATED`
  - `PAUSED → ENDING → ENDED / CANCELLED / TERMINATED`
  - `ENDING → ENDED / CANCELLED / TERMINATED`
- **Forbidden Transitions**:
  - Any transition from terminal states (`ENDED`, `EXPIRED`, `CANCELLED`, `TERMINATED`) back to `ACTIVE`, `PAUSED`, `STARTING`, or `ENDING` throws `InvalidDomainException("INVALID_TRANSITION")`.
  - Duplicate transitions to the same state are idempotent no-ops.

### Business & Validation Rules
1. **Gamer Validation**: Gamer must exist and be active (`CanOperate() == true`). Disabled gamers cannot start sessions.
2. **Workstation Validation**: Workstation must exist, be assigned to a Site, and be active (not deactivated, not disabled).
3. **Active Workstation Session Protection**: A workstation can have at most one active session (`STARTING`, `ACTIVE`, `PAUSED`, `ENDING`). Attempting to start a second session returns `409 Conflict`.
4. **Reservation Relationship**: If `ReservationId` is provided, reservation must exist, belong to the gamer and site, and be active/confirmed. Starting a session with a confirmed reservation transitions the reservation status to `ACTIVE`. Stopping the session transitions active reservations to `COMPLETED`.
5. **UTC Timestamps**: Timestamps (`StartedAt`, `PausedAt`, `EndedAt`, `CreatedAt`, `UpdatedAt`) are normalized to UTC (`DateTimeKind.Utc`).

---

## 4. Database Schema & Migration

### Schema (`sessions` Table)
- `Id`: `uuid` (Primary Key)
- `OrganizationId`: `uuid` (FK -> `Organizations`, Restrict)
- `SiteId`: `uuid` (FK -> `Sites`, Restrict)
- `WorkstationId`: `uuid` (FK -> `Workstations`, Restrict)
- `GamerId`: `uuid` (FK -> `Gamers`, Restrict)
- `ReservationId`: `uuid` (Nullable FK -> `Reservations`, Restrict)
- `Status`: `varchar(20)`
- `StartedAt`: `timestamp with time zone`
- `PausedAt`: `timestamp with time zone` (Nullable)
- `EndedAt`: `timestamp with time zone` (Nullable)
- `CreatedAt`: `timestamp with time zone`
- `UpdatedAt`: `timestamp with time zone` (Nullable)
- `RowVersion`: `xmin` / concurrency token

### Database Indexes
- `IX_sessions_Status` on `(Status)`
- `IX_sessions_WorkstationId_Status` on `(WorkstationId, Status)`
- `IX_sessions_GamerId_Status` on `(GamerId, Status)`
- `IX_sessions_ReservationId_Status` on `(ReservationId, Status)`

### Migration Status
- Migration `AddSessionDomain` generated and successfully applied to local PostgreSQL (`sayra_db`).

---

## 5. API Contracts

| Method | Endpoint | Description | Success Status | Error Codes |
|---|---|---|---|---|
| `POST` | `/api/sessions` | Start a new session | `201 Created` | `400` (`VALIDATION_FAILED`, `GAMER_DISABLED`), `409` (`WORKSTATION_HAS_ACTIVE_SESSION`) |
| `GET` | `/api/sessions/{id}` | Retrieve session by ID | `200 OK` | `404` (`NOT_FOUND`) |
| `POST` | `/api/sessions/{id}/pause` | Pause active session | `200 OK` | `404` (`NOT_FOUND`), `400` (`INVALID_TRANSITION`) |
| `POST` | `/api/sessions/{id}/resume` | Resume paused session | `200 OK` | `404` (`NOT_FOUND`), `400` (`INVALID_TRANSITION`) |
| `POST` | `/api/sessions/{id}/stop` | Stop session | `200 OK` | `404` (`NOT_FOUND`), `400` (`INVALID_TRANSITION`) |
| `POST` | `/api/sessions/{id}/cancel` | Cancel session | `200 OK` | `404` (`NOT_FOUND`), `400` (`INVALID_TRANSITION`) |
| `POST` | `/api/sessions/{id}/terminate` | Terminate session | `200 OK` | `404` (`NOT_FOUND`), `400` (`INVALID_TRANSITION`) |
| `GET` | `/api/sessions/workstation/{workstationId}/active` | Query active session for workstation | `200 OK` | `404` (`NO_ACTIVE_SESSION`) |
| `GET` | `/api/sessions/gamer/{gamerId}/active` | Query active session for gamer | `200 OK` | `404` (`NO_ACTIVE_SESSION`) |

---

## 6. Security, Concurrency & Audit

- **Security**: No sensitive client inputs trusted. Session ownership and organizational hierarchy enforced.
- **Concurrency**: `RowVersion` optimistic concurrency token mapped to `sessions` table. Active workstation uniqueness enforced at application domain service and database query levels.
- **Audit Logging**: Structured `AuditEvent` records persisted for every state change (`SessionCreated`, `SessionStarted`, `SessionPaused`, `SessionResumed`, `SessionStopped`, `SessionCancelled`, `SessionTerminated`).

---

## 7. Test Results

### Execution Summary
```
Test Run Summary:
-----------------
Total Solution Tests: 156
Passed: 156
Failed: 0
Skipped: 0
Duration: ~12 seconds
```

### Breakdown by Test Project
1. **`Sayra.Backend.ArchitectureTests`**: 3 / 3 Passed
2. **`Sayra.Backend.UnitTests`**: 98 / 98 Passed (includes 10 new Session unit tests)
3. **`Sayra.Backend.IntegrationTests`**: 55 / 55 Passed (includes 5 new Session integration tests against PostgreSQL)

---

## 8. Acceptance Criteria Verification

| Acceptance Criterion | Status | Verification |
|---|---|---|
| Build succeeds | **PASSED** | Clean solution compilation with 0 errors |
| Previous stages remain green | **PASSED** | All 141 previous unit, architecture, and integration tests passed |
| Session aggregate exists | **PASSED** | Implemented `Session` entity in `Sayra.Backend.Domain` |
| State machine is enforced | **PASSED** | Validated via `SessionUnitTests` and `SessionIntegrationTests` |
| Invalid transitions blocked | **PASSED** | Throws `INVALID_TRANSITION` and returns `400 Bad Request` |
| Workstation double-session prevented | **PASSED** | Returns `409 Conflict`; verified by unit and integration tests |
| Reservation relationship works | **PASSED** | Reservation status updated and tracked across session lifecycle |
| Database migration succeeds | **PASSED** | Migration `AddSessionDomain` applied to PostgreSQL |
| API operations work | **PASSED** | All endpoints verified end-to-end in integration tests |
| No timer/pricing/financial leaks | **PASSED** | No pricing, billing, or timer mechanisms added |

---

## 9. Known Limitations & Deferred Requirements

- **Server-Authoritative Timer & Pricing**: Session duration calculations, rate snapshots, pricing engines, and billing are deferred to STAGE 03-05 and later stages as specified.
- **TCP Protocol Transport**: Linking TCP client commands to session commands is deferred to STAGE 03-10.
