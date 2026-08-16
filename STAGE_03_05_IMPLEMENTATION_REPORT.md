# STAGE 03-05 IMPLEMENTATION REPORT — Server-Authoritative Timer & Session Segments

**Project:** SAYRA Central Backend
**Stage:** STAGE 03-05 (Server-Authoritative Timer & Session Segments)
**Author:** Senior Backend Engineer (Jules)
**Status:** Complete

---

## 1. Executive Summary

STAGE 03-05 implements the **Server-Authoritative Session Timing Engine** and **Session Segment Tracking** for the SAYRA Central Backend. The backend server is now the single authority for session timelines, duration tracking, active usage calculations, pause exclusions, remaining duration bounds, and expiration timing.

Client timestamps and duration claims are completely ignored. Timeline history is represented by append-oriented `SessionSegment` domain models (`ACTIVE` and `PAUSED`) persisted in PostgreSQL.

Out-of-scope features (Pricing Engine, Billing Calculation, Financial Ledger, Payment Processing, Automatic Expiration Workers, and TCP transport modifications) were strictly excluded, preserving modular domain boundaries.

---

## 2. Implemented Components & Files Changed

### Domain Layer (`Sayra.Backend.Domain`)
- **`Entities/SessionSegment.cs`**: Append-oriented entity representing a timeline segment (`ACTIVE` or `PAUSED`) with `SessionId`, `Type`, `StartedAtUtc`, `EndedAtUtc`, UTC normalization, and duration calculation methods.

### Contracts Layer (`Sayra.Backend.Contracts`)
- **`SessionContracts.cs`**:
  - `SessionTimingResponseDto`: Data contract for timing snapshot queries containing `SessionId`, `CurrentServerTimeUtc`, `StartedAtUtc`, `ConsumedDuration`, `PausedDuration`, `RemainingDuration`, and `ExpirationTimeUtc`.

### Application Layer (`Sayra.Backend.Application`)
- **`Sessions/SessionTimingSnapshot.cs`**: Value object encapsulating timing state snapshot.
- **`Sessions/ISessionTimeCalculator.cs`**: Interface and implementation (`SessionTimeCalculator`) for calculating active consumed duration, pause duration, remaining duration, and projected expiration point using server UTC clock authority.
- **`Sessions/SessionCommandsAndQueries.cs`**: Added queries `GetSessionCurrentStateQuery`, `GetSessionTimingQuery`, `GetSessionDurationQuery`, `GetSessionRemainingTimeQuery`.
- **`Sessions/SessionValidators.cs`**: Added FluentValidation rules for new timing queries.
- **`Sessions/SessionHandlers.cs`**: Updated all command handlers (`StartSession`, `PauseSession`, `ResumeSession`, `StopSession`, `CancelSession`, `TerminateSession`) to append and close session segments on state changes, with duplicate pause/resume idempotency protections. Implemented new timing query handlers.

### Infrastructure Layer (`Sayra.Backend.Infrastructure`)
- **`Persistence/Configurations/SessionSegmentConfiguration.cs`**: EF Core configuration for `session_segments` table, defining foreign key `SessionId` -> `Session.Id` with `DeleteBehavior.Restrict` and indexes on `(SessionId)`, `(SessionId, StartedAtUtc)`, and `(SessionId, Type)`.
- **`Persistence/ApplicationDbContext.cs`**: Added `DbSet<SessionSegment> SessionSegments`.
- **`DependencyInjection.cs`**: Registered `ISessionTimeCalculator` and timing query handlers.
- **`Migrations/20260816080048_AddSessionSegments.cs`**: EF Core migration creating `session_segments` table and indexes.

### API Layer (`Sayra.Backend.Api`)
- **`Controllers/SessionsController.cs`**: Added `GET /api/sessions/{id}/timing` endpoint returning `SessionTimingResponseDto`.

### Tests Layer (`tests/`)
- **`tests/Sayra.Backend.UnitTests/SessionUnitTests.cs`**: Added unit tests for `SessionSegment` validation, `SessionTimeCalculator` duration calculations, pause exclusions, allocated time remaining bounds, and non-negative duration constraints.
- **`tests/Sayra.Backend.IntegrationTests/SessionIntegrationTests.cs`**: Added integration tests for PostgreSQL `session_segments` persistence, `GET /api/sessions/{id}/timing` responses, and duplicate pause/resume idempotency.

---

## 3. Server-Authoritative Timing Architecture

### Timing Formulas
All timing calculations are derived dynamically from persisted `session_segments` at request time using the current **Backend Server UTC Clock** (`DateTime.UtcNow`).

1. **Active Consumed Duration**:
   $$\text{ConsumedDuration} = \sum_{\text{ACTIVE segments}} (\min(\text{EndedAtUtc}, \text{CurrentServerTime}) - \text{StartedAtUtc})$$
2. **Paused Duration**:
   $$\text{PausedDuration} = \sum_{\text{PAUSED segments}} (\min(\text{EndedAtUtc}, \text{CurrentServerTime}) - \text{StartedAtUtc})$$
3. **Remaining Duration**:
   $$\text{RemainingDuration} = \max(\text{AllocatedDuration} - \text{ConsumedDuration}, 0)$$
4. **Projected Expiration Point**:
   $$\text{ExpirationTimeUtc} = \text{CurrentServerTime} + \text{RemainingDuration}$$
   *(For active or paused sessions with allocated duration)*

---

## 4. Segment Rules & Lifecycle Management

- **Append-Oriented**: On session start, an open `ACTIVE` segment (`EndedAtUtc = null`) is created.
- **Pause Transition**: Closes open `ACTIVE` segment at server UTC time, appends open `PAUSED` segment.
- **Resume Transition**: Closes open `PAUSED` segment at server UTC time, appends open `ACTIVE` segment.
- **Stop / Cancel / Terminate**: Closes any open segment at server UTC time.
- **Idempotency & Duplicate Protection**: Duplicate pause requests while already paused, or duplicate resume requests while already active, do not create redundant or corrupting segments.

---

## 5. Database Schema & Migration

### Schema (`session_segments` Table)
- `Id`: `uuid` (Primary Key)
- `SessionId`: `uuid` (FK -> `Sessions`, Restrict)
- `Type`: `varchar(20)` (`ACTIVE`, `PAUSED`)
- `StartedAtUtc`: `timestamp with time zone`
- `EndedAtUtc`: `timestamp with time zone` (Nullable)
- `CreatedAt`: `timestamp with time zone`
- `UpdatedAt`: `timestamp with time zone` (Nullable)

### Database Indexes
- `IX_session_segments_SessionId` on `(SessionId)`
- `IX_session_segments_SessionId_StartedAtUtc` on `(SessionId, StartedAtUtc)`
- `IX_session_segments_SessionId_Type` on `(SessionId, Type)`

---

## 6. API Contracts

| Method | Endpoint | Description | Success Response | Error Responses |
|---|---|---|---|---|
| `GET` | `/api/sessions/{id}/timing` | Get current server-authoritative timing snapshot | `200 OK` (`SessionTimingResponseDto`) | `404` (`NOT_FOUND`) |
| `POST` | `/api/sessions/{id}/pause` | Pause active session & create PAUSED segment | `200 OK` (`SessionResponseDto`) | `404`, `400` |
| `POST` | `/api/sessions/{id}/resume` | Resume paused session & create ACTIVE segment | `200 OK` (`SessionResponseDto`) | `404`, `400` |
| `POST` | `/api/sessions/{id}/stop` | Stop session & close open segment | `200 OK` (`SessionResponseDto`) | `404`, `400` |

---

## 7. Test Results

### Execution Summary
```
Test Run Summary:
-----------------
Total Solution Tests: 165
Passed: 165
Failed: 0
Skipped: 0
Duration: ~10 seconds
```

### Breakdown by Test Project
1. **`Sayra.Backend.ArchitectureTests`**: 3 / 3 Passed
2. **`Sayra.Backend.UnitTests`**: 105 / 105 Passed (includes 7 new timing & segment unit tests)
3. **`Sayra.Backend.IntegrationTests`**: 57 / 57 Passed (includes 2 new timing & segment integration tests)

---

## 8. Acceptance Criteria Verification

| Acceptance Criterion | Status | Verification |
|---|---|---|
| Backend owns timing authority | **PASSED** | Computed solely from server UTC clock and persisted segments |
| Session segments are persisted | **PASSED** | Persisted in `session_segments` table; verified by integration tests |
| Active duration derived from server data | **PASSED** | `ISessionTimeCalculator` sums `ACTIVE` segment durations |
| Client timestamps ignored | **PASSED** | No client timestamps accepted or used in domain/application calculations |
| Pause/resume history preserved | **PASSED** | Immutable historical segments preserved in chronological order |
| Duplicate timing operations protected | **PASSED** | Idempotency verified in unit and integration tests |
| Database migration succeeds | **PASSED** | Migration `AddSessionSegments` applied to PostgreSQL |
| All tests pass | **PASSED** | All 165 tests passed green |
| Session compatibility maintained | **PASSED** | Existing session lifecycle endpoints remain fully backward-compatible |

---

## 9. Known Limitations & Deferred Requirements

- **Pricing Engine & Billing**: Session rate calculation, financial deductions, and ledger recording are deferred to STAGE 03-06 and later stages as specified.
- **Automatic Expiration Worker**: Expiration processing worker is deferred to later integration stages.
