# SAYRA Central Backend — Application Resilience Policy

## Executive Summary
This document establishes the canonical **Application Resilience Policy** for the **SAYRA Central Backend**. It defines the failure classification, retry safety, backoff with jitter, circuit breaker state machine, timeout bounds, nested retry prevention, and observability patterns used across all dependency interactions (PostgreSQL, Redis, local storage, and downstream calls).

---

## 1. Resilience Philosophy & Principles
1. **Controlled Failure over Unbounded Retries**: The backend must never convert transient errors into retry storms or cascading outages.
2. **Deterministic Failure Classification**: Errors are classified into explicit failure categories before deciding whether retry is permitted.
3. **Write Safety Enforcement**: Non-idempotent writes (especially financial debits, credits, and payments) are **NEVER** retried automatically on uncertain execution status.
4. **Bounded Operations**: Every dependency interaction is governed by per-attempt timeouts and an overall logical operation deadline.
5. **Observability First**: All retry attempts, circuit breaker transitions, fallbacks, and timeouts emit OpenTelemetry metrics and structured logs without secret leakage.

---

## 2. Failure Classification Taxonomy

| Category | Description | Examples | Action |
|---|---|---|---|
| **Retryable** | Transient network or dependency errors where repetition may succeed. | Socket timeout, temporary connection drop, Npgsql transient error, DB lock contention. | Bounded retry with exponential backoff and randomized jitter. |
| **NonRetryable** | Deterministic or client payload failures where retry will never succeed. | Invalid payload, schema violation, unauthorized, forbidden, domain constraint violation. | Immediate failure; throw exception or return error contract. |
| **FailFast** | Severe state or circuit condition where retrying amplifies damage. | Missing database connection string, open circuit breaker (`CircuitBreakerOpenException`). | Fast reject without invoking dependency operation. |
| **Degraded** | Dependency unavailable but operation can proceed safely via authoritative fallback. | Redis cache miss/error during configuration resolution. | Fallback to PostgreSQL authoritative source; record fallback metric. |
| **RequiresReconciliation** | In-doubt offline or stream state requiring Phase 09 reconciliation engine. | Out-of-order offline workstation batch payload. | Route through `IOfflineOrderingAndReconciliationEngine` or Dead Letter Queue. |

---

## 3. Operation Retry Safety Model

Before executing any retry, the resilience pipeline evaluates the `OperationRetrySafety` level:

```text
Operation Safety
 ├── SafeRead               → Bounded retry permitted on transient failure.
 ├── IdempotentWrite        → Bounded retry permitted (idempotency key / check prevents duplication).
 ├── ConditionallyRetryable → Retry permitted if operation verified as uncommitted.
 ├── NonRetryableWrite      → FORCED Max Attempts = 1 (Automatic retry strictly prohibited).
 └── RequiresReconciliation → Delegated to Phase 09 reconciliation semantics.
```

### Unsafe Retry Register
The following operations are classified as `NonRetryableWrite` and **MUST NOT** be automatically retried by application resilience pipelines:
- `DebitAccountCommand` / `CreditAccountCommand`
- `ProcessFinancialTransactionCommand`
- `CreatePaymentCommand`
- Non-idempotent session state mutations

---

## 4. Bounded Retry, Backoff & Jitter

When a retryable operation fails:
1. **Exponential Backoff**:
   $$\text{Delay} = \min(\text{InitialBackoff} \times 2^{\text{attempt}-1}, \text{MaxBackoff})$$
   - Default `InitialBackoff`: 0.5s
   - Default `MaxBackoff`: 5.0s
2. **Randomized Jitter**:
   - `JitterFactor`: 0.2 (±20% randomized spread around bounded backoff) to prevent synchronized client reconnection storms.
3. **Maximum Retry Attempts**:
   - Default: 3 attempts.

---

## 5. Timeout Strategy & Cancellation Propagation

Every operation is wrapped in a dual-timeout boundary:
- **Per-Attempt Timeout** (`AttemptTimeoutSeconds`, default 3.0s): Limits execution time of a single physical attempt.
- **Overall Operation Deadline** (`OverallTimeoutSeconds`, default 10.0s): Limits the total duration of the entire logical operation across all attempts and backoff delays.

### Cancellation Token Propagation
`CancellationToken` is passed through all async calls and `Task.Delay` backoff periods. If a caller cancels the operation, retry loops terminate immediately and rethrow `OperationCanceledException`.

---

## 6. Circuit Breaker State Machine

External and non-critical dependencies use a lightweight, thread-safe `CircuitBreaker`:

```text
       [Failure Count >= Threshold]
  ┌────────────────────────────────────┐
  │                                    │
  ▼                                    │
┌────────┐                          ┌──────┐
│ Closed │ ───(Success resets)────> │ Open │
└────────┘                          └──────┘
  ▲                                    │
  │                                    │ [Break Duration Elapsed]
  │        ┌──────────┐                │
  └─────── │ HalfOpen │ <──────────────┘
  (Trial)  └──────────┘
```

- **Failure Threshold**: Default 5 consecutive failures.
- **Break Duration**: Default 15 seconds.
- **HalfOpen Trial**: Allows 1 trial execution. If trial succeeds, state returns to `Closed`; if trial fails, state returns to `Open`.

---

## 7. Prevention of Nested Retry Multiplication

To prevent nested retry amplification (e.g., EF Core retry + Application retry + HTTP client retry):
- **EF Core Level**: `npgsqlOptions.EnableRetryOnFailure(3)` handles low-level transient database connection drops.
- **Application Boundary Level**: Wraps logical operations once. Application pipeline acknowledges EF Core retries and enforces a total maximum attempt boundary (default max 3 attempts overall).

---

## 8. Observability & OpenTelemetry Metrics

Resilience metrics are emitted under `Meter("Sayra.Backend.Resilience")`:
- `resilience_retry_attempts_total` (labels: `dependency`, `operation`, `attempt`, `category`)
- `resilience_retry_exhausted_total` (labels: `dependency`, `operation`, `attempts`)
- `resilience_timeouts_total` (labels: `dependency`, `operation`, `timeout_type`)
- `resilience_circuit_breaker_trips_total` (labels: `dependency`, `from_state`, `to_state`)
- `resilience_fallbacks_total` (labels: `dependency`, `operation`, `fallback_type`)
- `resilience_cancellations_total` (labels: `dependency`, `operation`)
