# Data Ownership, Financial Rules & Scalability Strategy

This document details data ownership boundaries, exact decimal financial rules, ephemeral state management, and high-performance scalability strategies for the **SAYRA Central Backend**.

---

## 1. Data Ownership Strategy

To preserve the maintainability and evolutionary agility of the Modular Monolith (enabling seamless extraction into independent microservices if ever required), strict module data ownership rules are enforced.

### 1.1. Module Isolation Rules
1. **Exclusive Schema Ownership**: Every domain module (`Identity`, `Workstations`, `Sessions`, `Billing`, `Pricing`, `Reservations`, `Telemetry`, `Configuration`) is entirely responsible for its tables, migrations, and entities.
2. **No Foreign Keys Across Modules**: PostgreSQL database foreign keys across module table boundaries are **strictly forbidden**. Cross-domain references must use stable UUID identifiers (e.g., `WorkstationId`, `GamerId`, `SessionId`).
3. **No Direct SQL Joins**: Direct cross-module SQL joins are prohibited in EF Core and raw SQL queries.
4. **Data Synchronization**: Shared cross-module state is kept synchronized asynchronously via **Domain Events** or read synchronously via registered **Public Application Interfaces**.

### 1.2. State Category Matrix

| Data Category | Primary Source of Truth | Backup / Snapshot | Ephemeral / Cache State | Redis Key Pattern |
|---|---|---|---|---|
| **Financial Ledger & Balances** | PostgreSQL (`FinancialTransactions`, `Payments`, `GamerAccounts`) | S3 / Cold Backup | *Never cached for auth or updates* | N/A |
| **Active Player Sessions** | PostgreSQL (`WorkstationSessions`) | Redis Heartbeat Snapshot | Redis Session Metadata | `session:active:{sessionId}` |
| **Active TCP Connection Tunnels** | Monolith Process RAM | None (Auto-rebuilds on connect) | Redis Server Node Affinity | `sayra:v1:connection:{connectionId}:state` |
| **Workstation Fleet Registry** | PostgreSQL (`Workstations`) | None | Redis Online Status | `sayra:v1:workstation:{pcId}:latest` |
| **Telemetry Metrics & Events** | PostgreSQL (`TelemetryMetrics`, `AuditEvents`) | None | Redis Stream / Ephemeral Key (15m TTL) | `v1:telemetry:{pcId}:latest` |
| **Configuration Packages & Targets** | PostgreSQL (`ConfigurationPackages`, `Targets`) | Database Migrations | Redis Distributed Cache (Cache-Aside) | `sayra:config:v1:{orgId}:{scopeType}:{scopeId}` |

---

## 2. Financial Correctness & Monetary Invariants

### 2.1. Absolute Floating-Point Prohibition
* **Mandate**: Floating-point types (`float`, `double`, `real`) are **strictly forbidden** for monetary representation or calculations. Floating-point binary representation introduces cumulative rounding drift.
* **Standard**: All financial values, rates, discounts, credit additions, debits, and account balances MUST use the .NET `decimal` type (mapped to PostgreSQL `NUMERIC(18, 4)`).

### 2.2. Server Timing Authority & Transactional Billing
* **Server Time Authority**: All session duration calculations, billing increments, and financial timestamps execute using the server's authoritative UTC clock (`DateTime.UtcNow`). Client-side workstation clocks are untrusted.
* **Idempotency Safeguards**:
  * Financial deposits, debits, and payments enforce strict database-level unique constraints on `IdempotencyKey`.
  * Request fingerprints (SHA-256 hash of payload parameters) are compared on key re-use. Identical payloads return the previous result safely; conflicting payloads return `HTTP 409 Conflict`.
* **Ledger Mechanics**:
  * Balance updates in `GamerAccount` and `FinancialTransaction` execute inside isolated database transactions (`ExecuteInTransactionAsync`).
  * Post-paid usage charges (`USAGE_CHARGE`) permit negative account balances to record consumed usage debt upon session termination. Pre-paid debits enforce strict non-negative balance checks (`INSUFFICIENT_BALANCE`).

---

## 3. High-Volume Ingestion & Horizontal Scalability

### 3.1. Telemetry & Client Event Ingestion Pipeline
* **Ingestion**: Persistent TLS 1.3 TCP socket handlers receive telemetry reports every 10–30 seconds per workstation.
* **Allocation-Free Framing**: Byte streams are parsed using `System.IO.Pipelines` and pre-allocated byte buffers (`ArrayPool<byte>`) to prevent Garbage Collection (GC) pauses under high telemetry loads.
* **Redis Caching & Fast-Path Deduplication**:
  * Telemetry metric snapshots are cached in Redis (`v1:telemetry:{pcId}:latest`) with a 15-minute TTL for fast UI queries.
  * Ingested client events enforce Redis deduplication (`v1:event:dedup:{eventId}`) with a 24-hour TTL before inserting into PostgreSQL `AuditEvents`.

### 3.2. Real-Time Command Routing & Redis Pub/Sub
* **Connection Affinity**: Persistent TCP sockets reside on the specific monolith instance where the socket connection was established.
* **Cross-Node Command Routing**:
  1. When an administrator issues a command (e.g., `LOCK_WORKSTATION`) via REST API to **Node A**, Node A checks Redis to locate the active connection node for target `PcId`.
  2. If target socket is connected to **Node B**, Node A publishes the command payload to Redis Pub/Sub channel `workstation:commands:{pcId}`.
  3. **Node B** (which holds the active TCP socket) receives the Redis message and immediately transmits the `SecureMessageEnvelope` down the TCP socket.
