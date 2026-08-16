# STAGE 03-06 Implementation Report — Pricing / Tariff / Rate Snapshot Engine

## 1. Overview
This report documents the implementation of **STAGE 03-06 (Pricing / Tariff / Rate Snapshot Engine)** for the SAYRA Central Backend. The pricing module operates as a standalone domain in the Modular Monolith architecture, decoupled from Billing, Ledger, Wallet, or Financial modules.

---

## 2. Implemented Components

### 2.1 Domain Layer
- **`PricingPlan`**: Aggregate root representing tariff plans belonging to a `Site`. Supports `Active` and `Inactive` status lifecycle.
- **`PricingRule`**: Defines matching rule dimensions (`WorkstationId`, `ZoneId`, `GamerType`, `DayOfWeek`, `StartTime`, `EndTime`, `IsPeak`) and priority ordering.
- **`RateSnapshot`**: Frozen, immutable rate snapshot associated with a `Session`.
- **`Session` Entity Update**: Added optional `PricingPlanId` property to link sessions with pricing plans without introducing financial calculations.
- **Domain Events**: Created `PricingPlanCreated`, `PricingRuleCreated`, `PricingPlanActivated`, `PricingPlanDeactivated`, `RateResolved`, and `RateSnapshotCreated` events.

### 2.2 API Contracts (`Sayra.Backend.Contracts`)
- **DTOs**: `CreatePricingPlanRequestDto`, `CreatePricingRuleRequestDto`, `PricingPlanResponseDto`, `PricingRuleResponseDto`, `RateSnapshotResponseDto`, `ResolveRateRequestDto`, `ResolvedRateResponseDto`.

### 2.3 Application Layer
- **`IRateResolver` / `RateResolver`**: Deterministic rate resolution engine evaluating active pricing plans and ordered rules by priority.
- **`IRateSnapshotService` / `RateSnapshotService`**: Immutable rate snapshot creation and retrieval service.
- **CQRS Handlers**: Handlers for `CreatePricingPlanCommand`, `CreatePricingRuleCommand`, `ActivatePricingPlanCommand`, `DeactivatePricingPlanCommand`, `GetPricingPlanQuery`, `GetPricingRulesQuery`, `ResolveRateQuery`.

### 2.4 Infrastructure & Database Layer
- **EF Core Configurations**:
  - `PricingPlanConfiguration`: Table `pricing_plans`, unique index `(SiteId, Name)`, FK to `Site`.
  - `PricingRuleConfiguration`: Table `pricing_rules`, unique index `(PricingPlanId, Priority)`, `numeric(18, 4)` for `RateAmount`, FK to `PricingPlan` (cascade delete).
  - `RateSnapshotConfiguration`: Table `rate_snapshots`, unique index `(SessionId)`, `numeric(18, 4)` for `RateAmount`, FKs to `Session` and `PricingPlan`.
  - `SessionConfiguration`: Mapped optional `PricingPlanId`.
- **Database Migration**: Created and applied `AddPricingTariffEngine` migration.

### 2.5 API Controller
- **`PricingController`** (`/api/pricing`):
  - `POST /api/pricing/plans`
  - `POST /api/pricing/plans/{id}/rules`
  - `POST /api/pricing/plans/{id}/activate`
  - `POST /api/pricing/plans/{id}/deactivate`
  - `GET /api/pricing/plans/{id}`
  - `GET /api/pricing/plans/{id}/rules`
  - `GET /api/pricing/resolve`

---

## 3. Pricing Architecture & Rule Resolution Strategy

```
  [ Client / API Request ]
            │
            ▼
    [ ResolveRateQuery ]
            │
            ▼
     [ IRateResolver ]
            │
            ├─► 1. Query Active PricingPlan for Site
            ├─► 2. Query Rules for Plan ordered by Priority (1, 2, 3...)
            └─► 3. Evaluate Rule Dimensions (Workstation > Zone > GamerType > TimeRange > DayOfWeek > Peak)
            │
            ▼
  [ First Matching Rule Output ] ──► ResolvedRateResponseDto
```

- **Determinism**: Given identical site, workstation, zone, gamer type, and timestamp inputs, the resolver produces the exact same rate result.
- **Priority Ordering**: Rules are evaluated strictly ascending by integer `Priority` value (Priority 1 is evaluated before Priority 2).
- **Dimension Matching**: Unspecified rule dimensions act as wildcards/defaults; specified dimensions must match input parameters.

---

## 4. Rate Snapshot Behavior
- When a rate is applied to a session, `RateSnapshotService.CreateSnapshotAsync` generates a snapshot freezing `RateAmount`, `Currency`, `PricingPlanId`, `PricingRuleId`, `AppliedAtUtc`, and `RuleReference`.
- **Immutability**: Subsequent price updates or plan changes do NOT affect existing sessions or snapshots. Requesting snapshot creation for an already snapshotted session returns the original snapshot without modification.

---

## 5. Security & Concurrency Handling
- **Money Safety**: Arithmetic uses `Money` Value Objects / `decimal` with explicit 4-decimal precision. No `float` or `double` types used.
- **Database Constraints**: Uniqueness constraints on `(SiteId, Name)` and `(PricingPlanId, Priority)` prevent duplicate plans and rules under concurrent updates.
- **Concurrency**: EF Core tracking and optimistic concurrency tokens ensure consistent state updates.

---

## 6. Test Suite & Verification Results

### Execution Summary
- **Unit Tests**: 118 Passed (0 Failed)
- **Architecture Tests**: 3 Passed (0 Failed)
- **Integration Tests**: 61 Passed (0 Failed)
- **Total Test Count**: 182 Passed (0 Failed)

### Pricing Specific Tests Implemented
- `PricingPlan_NormalizeAndValidate_Valid_Plan_Should_Succeed`
- `PricingPlan_Activate_And_Deactivate_Should_Update_Status`
- `PricingRule_Matches_Workstation_Dimension`
- `PricingRule_Matches_Zone_Dimension`
- `PricingRule_Matches_GamerType_Dimension`
- `PricingRule_Matches_DayOfWeek_Dimension`
- `PricingRule_Matches_TimeRange_Dimension`
- `RateResolver_Priority_Ordering_First_Match_Wins`
- `RateSnapshotService_Creation_And_Immutability`
- `Full_Pricing_Plan_Rules_Activation_And_Rate_Resolution_Flow` (API Integration Flow)
- `Duplicate_PricingPlan_Name_For_Same_Site_Should_Return_Conflict` (409 Conflict)
- `Duplicate_PricingRule_Priority_Should_Return_Conflict` (409 Conflict)
- `RateSnapshot_Persistence_Precision_And_Immutability_In_Database` (PostgreSQL `numeric(18, 4)` precision)

---

## 7. Migration Status & Acceptance Criteria Verification

| Requirement / Acceptance Criteria | Status | Verification Detail |
|---|---|---|
| Pricing domain decoupled from Billing/Ledger | VERIFIED | No billing or financial logic in Pricing module |
| Pricing plan creation, activation, deactivation | VERIFIED | `PricingPlan` entity & endpoints implemented and tested |
| Pricing rules with dimensions & priority | VERIFIED | `PricingRule` entity & priority evaluation implemented |
| Deterministic rate resolution | VERIFIED | `RateResolver` unit and integration tests passing |
| Immutable rate snapshots | VERIFIED | `RateSnapshot` entity, service, & DB constraints verified |
| Precision using PostgreSQL `numeric` | VERIFIED | EF Core mapped to `numeric(18, 4)` |
| Database migration applied | VERIFIED | `AddPricingTariffEngine` executed on PostgreSQL |
| Tests passing | VERIFIED | All 182 unit/arch/integration tests pass |

---

## 8. Known Limitations & Deferred Decisions
- **Billing Calculations**: Final cost calculation based on elapsed time segments is intentionally deferred to Stage 03-07 (Billing Engine).
- **Client Synchronization**: Real-time push notification of rate changes to connected TCP workstation clients is out of scope for this stage.
