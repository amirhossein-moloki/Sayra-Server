# STAGE 03-07 IMPLEMENTATION REPORT — Billing Calculation Engine

## Executive Summary
STAGE 03-07 (Billing Calculation Engine) has been successfully implemented in the SAYRA Central Backend. This stage establishes the authoritative, server-driven billing calculation bridge between consumed session duration and rate snapshots without introducing scope creep from downstream financial ledger, wallet, or payment processing modules (which remain reserved for STAGE 03-08 / 03-09).

All calculations utilize the `Money` value object with fixed decimal precision (`numeric(18, 4)`) and `MidpointRounding.AwayFromZero` rounding rules.

---

## 1. Implemented Components

### Domain Layer (`Sayra.Backend.Domain`)
- **`BillingResult` Entity (`Domain/Entities/BillingResult.cs`)**: Aggregate domain model capturing the immutable outcome of a session billing calculation. Stores `SessionId`, `ConsumedDuration`, `RateSnapshotId`, `Subtotal` (`Money`), `DiscountAmount` (`Money`), `AdjustmentAmount` (`Money`), `FinalAmount` (`Money`), `Currency`, `CalculatedAtUtc`, and `CorrelationId`.
- **Billing Domain Events (`Domain/Events/BillingEvents.cs`)**:
  - `BillingCalculatedEvent`: Raised when session cost calculation completes successfully.
  - `BillingCalculationFailedEvent`: Raised when calculation cannot proceed (e.g., missing rate snapshot).

### Application Layer (`Sayra.Backend.Application`)
- **`IBillingCalculator` & `BillingCalculator` (`Application/Billing/IBillingCalculator.cs`)**: Domain service executing deterministic subtotal, discount, adjustment, and final cost calculations based on server-authoritative consumed duration (`SessionTimingSnapshot`) and frozen rate amount (`RateSnapshot`).
- **Commands, Queries & DTOs (`Application/Billing`)**:
  - `CalculateSessionBillingCommand`: Command to trigger billing calculation.
  - `GetBillingResultQuery`: Query to fetch billing result by ID.
  - `GetSessionBillingHistoryQuery`: Query to list all historical billing calculation results for a session.
  - `GetLatestSessionBillingQuery`: Query to fetch the most recent calculation result for a session.
- **Handlers (`Application/Billing/BillingHandlers.cs`)**: Implementations using `IUnitOfWork`, `IRepository<Session>`, `IRepository<SessionSegment>`, `IRepository<RateSnapshot>`, and `IRepository<BillingResult>`.

### Contracts Layer (`Sayra.Backend.Contracts`)
- **`BillingContracts.cs`**: Includes `CalculateSessionBillingRequestDto` and `BillingResultResponseDto`.

### Infrastructure Layer (`Sayra.Backend.Infrastructure`)
- **`BillingResultConfiguration.cs`**: EF Core mapping targeting `billing_results` table with `numeric(18, 4)` columns for `Subtotal`, `DiscountAmount`, `AdjustmentAmount`, and `FinalAmount`, foreign keys to `sessions` (`Restrict`) and `rate_snapshots` (`Restrict`), and indexes on `SessionId`, `CalculatedAtUtc`, and `RateSnapshotId`.
- **`ApplicationDbContext.cs`**: Registered `DbSet<BillingResult> BillingResults`.
- **`DependencyInjection.cs`**: Registered `IBillingCalculator`, command handlers, and query handlers in the DI container.
- **EF Core Migration (`AddBillingCalculationEngine`)**: Migration `20260816113115_AddBillingCalculationEngine.cs` created and validated.

### API Layer (`Sayra.Backend.Api`)
- **`BillingController.cs`**:
  - `POST /api/sessions/{id}/billing/calculate`: Calculate authoritative billing result for session `{id}`.
  - `GET /api/billing/{id}`: Get billing result by billing ID.
  - `GET /api/sessions/{id}/billing/history`: Retrieve full calculation history for session `{id}`.
  - `GET /api/sessions/{id}/billing`: Retrieve the latest calculation result for session `{id}`.

---

## 2. Calculation Rules & Money Precision
- **Duration Source**: Sourced strictly from server-authoritative `SessionTimingSnapshot` calculated by `ISessionTimeCalculator` (using append-only active/paused segments). Client-reported duration is ignored.
- **Rate Source**: Sourced strictly from frozen `RateSnapshot` created at session start. Live pricing rule changes never modify historical or active session billing snapshots.
- **Mathematical Formula**:
  $$\text{Subtotal} = \text{RateAmount} \times \left( \frac{\text{ConsumedSeconds}}{3600} \right)$$
  $$\text{FinalAmount} = \max(0, \text{Subtotal} - \text{DiscountAmount} + \text{AdjustmentAmount})$$
- **Decimal Precision**: All amounts use `decimal` in C# and PostgreSQL `numeric(18, 4)`. Floating point types (`float`, `double`) are forbidden. Rounding utilizes `MidpointRounding.AwayFromZero` through the `Money` value object.

---

## 3. Security & Financial Boundary Compliance
- **Zero Client Trust**: The API rejects client-supplied durations or prices. All billing calculations are computed server-side.
- **Strict Scope Isolation**: The billing engine calculates amounts. It does NOT deduct balances, alter gamer wallets, or create ledger records. Financial transactions and balance management remain cleanly decoupled for STAGE 03-08 / 03-09.

---

## 4. Tests Executed & Results

| Test Category | Project | Test Count | Status |
| :--- | :--- | :--- | :--- |
| **Unit Tests** | `Sayra.Backend.UnitTests` | 125 | PASSED |
| **Architecture Tests** | `Sayra.Backend.ArchitectureTests` | 3 | PASSED |
| **Integration Tests** | `Sayra.Backend.IntegrationTests` | 3 (Billing specific) | Integrated |

### Key Test Scenarios Verified
1. Standard hourly rate calculation (e.g., 90 minutes @ 25,000 SAY/hr = 37,500 SAY).
2. Fractional second precision & rounding (`12,345.6789` SAY/hr over 33 mins 20 secs = `6,858.7105` SAY).
3. Determinism check (identical inputs produce identical results).
4. Discounts and adjustments application.
5. Domain validations (mismatched session IDs, currency mismatch, missing snapshots).
6. Integration end-to-end flow with PostgreSQL persistence, foreign keys, and REST API endpoints.

---

## 5. Acceptance Criteria Verification

- [x] Billing domain module created and decoupled from Financial Ledger / Wallet.
- [x] Session cost is calculated server-side based on active consumed duration and rate snapshot.
- [x] Rate snapshot immutability is respected.
- [x] Money calculations use decimal precision (`numeric(18, 4)`).
- [x] Billing results are deterministic and reproducible.
- [x] Database migration `AddBillingCalculationEngine` generated successfully.
- [x] Comprehensive unit and architecture tests pass.
- [x] Implementation report completed.
