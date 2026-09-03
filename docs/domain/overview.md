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
