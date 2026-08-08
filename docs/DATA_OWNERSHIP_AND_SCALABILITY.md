# Data Ownership & Scalability Strategy

This document details state management, data boundary rules, exact decimal currency representation, and high-performance scalability strategies for the **SAYRA Central Backend**.

---

## 1. Data Ownership Strategy

To ensure a highly maintainable Modular Monolith that can be separated into independent microservices in the future, we enforce strict data ownership rules.

### 1.1. Module-Specific Schemas
1. Every module (e.g., `Billing`, `Sessions`, `Workstations`) is entirely responsible for its tables and files.
2. Direct SQL joins across module boundaries are **prohibited**.
3. Foreign keys cannot reference tables across module boundaries. Instead, modules reference cross-domain records using stable UUIDs (e.g., `WorkstationId`, `SessionId`).
4. Read-only cached copies of cross-module data are permitted only if kept synchronized via asynchronous **Domain Events**.

### 1.2. State Category Matrix

| Data Type | Primary Source | Backup/Snapshot State | Ephemeral/Cache State | Shared Key Store |
|---|---|---|---|---|
| **Financial Records** | PostgreSQL | S3 / Cold Storage | (Never cached for auth) | None |
| **Active Sessions** | PostgreSQL | Redis (Heartbeat snapshots) | Redis | Redis (`session:active:{id}`) |
| **Active TCP Connection** | Monolith Process RAM | None (Auto-rebuilds) | Redis | Redis (`tcp:affinity:{node_id}`) |
| **Telemetry (Unprocessed)** | Monolith Buffer / PostgreSQL | None | Redis (Stream) | Redis (`telemetry:queue`) |
| **Workstation Registry** | PostgreSQL | None | Redis (Online Status) | Redis (`workstation:online:{id}`) |

---

## 2. Scalability Strategy

The backend is designed to handle hundreds of active gaming workstations and thousands of simultaneous client sessions.

### 2.1. Handling High Volume Telemetry
* Telemetry reports arrive from every client workstation every 10–30 seconds.
* **Ingestion Strategy**: Raw TCP connection handlers immediately parse incoming `TELEMETRY_REPORT` packets. Instead of executing direct PostgreSQL writes per packet, the data is pushed to a highly optimized **Redis Stream** or an in-memory batching queue.
* **Batch Processing**: An isolated background worker pulls records from the Redis queue, batches them into 100-item chunks, and writes them to PostgreSQL using EF Core `BulkInsert` or `Npgsql` raw COPY APIs.
* **Downsampling**: To prevent database size bloat, a scheduled task aggregates telemetry metrics older than 7 days into daily/hourly summaries, deleting raw granular logs.

### 2.2. Horizontal Scaling and Stateful TCP Connections
1. **TCP Connection Affinity**:
   * TCP connections are naturally stateful. A client must maintain its TLS 1.3 tunnel with the specific backend node it connected to.
   * If there are multiple Monolith nodes behind a load balancer, each node keeps an in-memory dictionary of active client sockets.
2. **Redis-Backed Real-Time Command Routing**:
   * If an administrator sends an API command (e.g., `LOCK_WORKSTATION`) to Node A, but the target workstation's TCP socket is connected to Node B, Node A cannot talk directly to Node B.
   * **Solution**: Node A publishes a message to **Redis Pub/Sub** under a channel like `workstation:commands:{workstation_id}`.
   * Node B, which holds the active TCP connection, is subscribed to that channel. It receives the command from Redis and sends it immediately down the TCP socket.
3. **Heartbeat Management**:
   * Workstation online states are updated dynamically in Redis using `SETEX` with a 30-second TTL. The background `HeartbeatMonitorService` checks Redis to declare a workstation offline if no heartbeat was received.

---

## 3. Financial Correctness & Monetary Rules

### 3.1. Floating Point Prohibition
* **Rule**: Floating-point types (`double`, `float`, `real`) are strictly prohibited for monetary representation. Floating-point mathematics introduces rounding errors which accumulate over time, leading to financial discrepancies.
* **Standard**: All monetary values MUST be declared using the `.NET` `decimal` type (equivalent to SQL `NUMERIC(18, 4)` or `DECIMAL(18, 4)`).

### 3.2. Transactional Billing
* Calculations for hourly billing must be executed transactionally.
* Session billing increments (such as cost-per-minute) must be calculated on the server side using the server's authoritative clock (UTC) to prevent client-side time tamper.
* The DB transaction must use an `Isolated` read-committed block to update the user account credit and log an immutable line item to the `FinancialAudit` table in a single atomic transaction.
