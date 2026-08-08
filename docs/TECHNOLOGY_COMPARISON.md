# Technology Comparison

This document details the comparative evaluation of different programming languages, runtimes, database technologies, and architectural styles considered for the **SAYRA Central Backend**.

---

## 1. Programming Language & Runtime Candidates

| Criteria | C# (.NET 8.0) [Selected] | Go (Golang) | Node.js (TypeScript) | Rust | JVM (Java / Kotlin) |
|---|---|---|---|---|---|
| **Networking & TLS 1.3** | **Excellent**: First-class async sockets, `System.IO.Pipelines`, native TLS 1.3 with SslStream. | **Excellent**: Superb concurrent socket networking with goroutines and native TLS 1.3. | **Moderate**: Single-threaded event loop can struggle with heavy TCP parsing/crypto under load. | **Excellent**: Ultra-high performance, but highly complex memory management. | **Good**: Strong networking, but higher memory footprint and cold start overhead. |
| **Transactional Integrity** | **Excellent**: Rich ecosystem, compile-time checked LINQ queries, EF Core 8 transaction management. | **Moderate**: Lacks a mature, fully featured ORM like EF Core; standard SQL/gorm is verbose. | **Moderate**: Pragmatic ORMs (Prisma, TypeORM) but lacks compilation-level type guarantees of LINQ. | **High**: Excellent safety, but highly verbose and slows down business logic iteration. | **Excellent**: Hibernate/JPA is mature but heavy, verbose, and memory-intensive. |
| **Development Velocity** | **High**: Modern language features, strong typing, massive standard library, and exceptional tooling. | **High**: Simple language, fast compiler, but boilerplate-heavy for rich business domains. | **High**: Quick startup, massive npm ecosystem, but dynamic typing challenges at scale. | **Low**: High learning curve, borrow checker overhead slows down business logic changes. | **Moderate**: Verbose, slow startup, boilerplate-heavy compared to modern .NET/C#. |
| **Operational Simplicity** | **High**: Single compiled binary (or self-contained folder). Lightweight Docker Alpine footprint. | **Excellent**: Single statically linked binary, ultra-low resource footprint. | **Moderate**: Huge `node_modules` folders, complex dependency security audits. | **Excellent**: Minimal footprint, but slow build/compile times in CI/CD pipeline. | **Moderate**: Requires heavy JVM configuration, higher memory baselines. |

### Decision Rationale: Why C# / .NET 8.0?
* **C# / .NET 8.0** represents the best union of performance and developer productivity. It has enterprise-grade ORMs, native TLS 1.3 support, and high-performance custom socket primitives in the box.
* **Go** was a strong runner-up due to its light weight, but building complex billing and domain logic in Go leads to excessive boilerplate.
* **Node.js** lacks the native multi-threaded scaling for thousands of persistent TLS TCP connections and fails to provide compile-time safety for complex financial computations.
* **Rust** is overly complex for a business application with rapidly evolving features.

---

## 2. Architecture: Modular Monolith vs. Microservices vs. Hybrid

We evaluated three architectural topologies for the SAYRA Central Backend:

### Option A — Modular Monolith [Selected]
The backend is structured as a single process, but divided into distinct, loosely-coupled assemblies (projects or folders) with strict physical separation. Communication across modules is asynchronous via an in-memory event bus or synchronous via interface calls.

* **Operational Complexity**: **Low** - Single process to run, log, monitor, and deploy. Easily packaged into Docker Compose for a LAN-based internet-independent gaming center.
* **Transactions & Financial Consistency**: **Excellent** - Can use simple, database-level ACID transactions across module boundaries where absolutely necessary, though discouraged in favor of eventual consistency.
* **Database Consistency**: **High** - Single database server hosting independent schemas/logical tables, avoiding network splits and distributed transaction protocols.
* **Network Overhead**: **Negligible** - High-speed in-memory data transfer.
* **Future Service Extraction**: **High** - Strict package reference hierarchies and interface communication boundaries make extracting any module into a separate microservice extremely straightforward.

### Option B — Microservices
The backend is split into multiple independently-run services (Workstation Service, Billing Service, Telemetry Service), each with its own database and communication protocol.

* **Operational Complexity**: **Extreme** - Requires managing 10+ services, service discovery, distributed tracing, Kubernetes/Nomad, and dealing with network partitions. Too complex for a local gaming-center deployment.
* **Transactions & Financial Consistency**: **Low/Complex** - Requires complex distributed transaction patterns (Sagas, Outbox, 2PC) to keep billing, sessions, and workstations in sync.
* **Network Overhead**: **High** - Every cross-module communication involves serialization, network hops, and error handling.
* **Failure Isolation**: **High** - A crash in the Telemetry Service won't affect the Billing Service.

### Option C — Hybrid
A monolith for core domain logic (Billing, Session, Workstations) alongside an independent service for high-volume telemetry or TCP persistent connections.

* **Operational Complexity**: **Medium** - Increases complexity by introducing 2-3 distinct runtimes, requiring inter-process communication.
* **Tradeoffs**: Highly scalable, but introduces premature optimization for our initial phase of managing hundreds of clients.

### Selected Architecture: Option A (Modular Monolith)
For the current scale of gaming centers (tens to hundreds of workstations, thousands of concurrent player sessions), **Option A (Modular Monolith)** is the correct choice. It provides maximum development velocity and operational simplicity while ensuring a clear extraction path.

---

## 3. Database Candidates

| Criteria | PostgreSQL [Selected] | Microsoft SQL Server | MySQL / MariaDB |
|---|---|---|---|
| **ACID/Financial Safety** | **Excellent**: Industry standard, robust MVCC, fully compliant transactional safety. | **Excellent**: Extremely reliable, but expensive licensing and platform-coupling history. | **Moderate**: Good, but historically less robust optimizer and locking defaults. |
| **JSON Support** | **Excellent**: Rich JSONB indexes (GIN) for semi-structured telemetry data. | **Good**: JSON query functions, but JSON columns are stored as NVARCHAR with less optimization. | **Good**: Basic JSON support, but GIN-equivalent indexing is weaker. |
| **Licensing / Cost** | **Free / Open Source**: Permissive license, no scaling cost limits. | **Expensive**: Complex licensing per core, highly restrictive on free Express editions. | **Free**: Open source (GPL), but enterprise support tied to Oracle. |
| **Ecosystem & Tooling** | **Outstanding**: Integrates perfectly with modern EF Core 8 (`Npgsql`). | **Excellent**: Native Microsoft tooling, but less active community-driven open innovations. | **Good**: Widely supported, but weaker ORM capabilities relative to Postgres. |

### Decision Rationale: Why PostgreSQL?
PostgreSQL v16+ provides a robust, developer-friendly, and completely free relational engine. It excels at both **structured financial records** (via ACID transactions and standard indexing) and **unstructured telemetry/audit payloads** (via JSONB).

---

## 4. ORM & Data Access Candidates

We compared **EF Core 8** and **Dapper**:
* **Dapper** is a micro-ORM offering near-raw ADO.NET performance, but requires writing manual SQL and managing migrations.
* **EF Core 8** provides rich LINQ querying, migration management, and dirty-tracking, with performance within 1-2% of raw SQL for optimized queries.
* **Selected: EF Core 8** as the primary ORM for maximum development velocity, safety, and migration consistency. **Dapper** will be allowed inside the Telemetry module as an escape hatch for raw SQL high-velocity ingestion if required.

---

## 5. Summary of Other Tech Choices

* **Cache (Redis)**: De-facto standard for distributed caching, heartbeat states, distributed locks (`RedLock`), and real-time command routing (via Redis Pub/Sub).
* **Background Processing**: ASP.NET Core `IHostedService` / `BackgroundService` processes local tasks, utilizing PostgreSQL or Redis for persistent task queues. Avoids bringing in heavy messaging infrastructure (like RabbitMQ or Kafka) prematurely.
* **API Architecture (REST + JWT)**: Pragmatic, stateless, highly standard, compatible with the client-side authentication flow, and simple to secure.
* **TCP Layer**: Raw custom TCP sockets running over .NET Core, leveraging `SslStream` for TLS 1.3 handshake termination, translating incoming byte buffers into typed messages via JSON serialization.
* **UDP Discovery**: Custom UDP Socket listener running on port 37020, bound to local network interfaces, responding with signed RSA payloads.
