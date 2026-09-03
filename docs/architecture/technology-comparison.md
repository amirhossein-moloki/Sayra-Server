# Technology Comparison & Evaluation Rationale

This document details the comparative technical evaluation of programming languages, runtimes, database engines, ORMs, and architectural styles considered for the **SAYRA Central Backend**.

---

## 1. Programming Language & Runtime Candidates

| Criteria | C# (.NET 8.0) [Selected] | Go (Golang) | Node.js (TypeScript) | Rust | JVM (Java / Kotlin) |
|---|---|---|---|---|---|
| **Networking & TLS 1.3** | **Excellent**: First-class async sockets, `System.IO.Pipelines`, native TLS 1.3 with `SslStream`. | **Excellent**: Native concurrent socket networking with goroutines and native TLS 1.3. | **Moderate**: Single-threaded event loop can struggle with heavy TCP parsing/crypto under load. | **Excellent**: Ultra-high performance, but complex memory management for business logic. | **Good**: Strong networking, higher memory baseline and cold start overhead. |
| **Transactional Integrity** | **Excellent**: Rich ecosystem, compile-time LINQ, EF Core 8 transaction management. | **Moderate**: Lacks a mature, fully featured ORM like EF Core; raw SQL/gorm is verbose. | **Moderate**: ORMs (Prisma, TypeORM) lack compilation-level type guarantees of LINQ. | **High**: Excellent safety, but verbose for rapid business logic iteration. | **Excellent**: Hibernate/JPA is mature but heavy and memory-intensive. |
| **Development Velocity** | **High**: Modern language features, strong typing, massive standard library, exceptional tooling. | **High**: Simple language, fast compiler, but boilerplate-heavy for rich business domains. | **High**: Quick startup, massive npm ecosystem, but dynamic typing challenges at scale. | **Low**: High learning curve, borrow checker overhead slows down domain iteration. | **Moderate**: Verbose, slower startup compared to modern .NET/C#. |
| **Operational Simplicity** | **High**: Single compiled binary/folder. Lightweight Docker Alpine footprint. | **Excellent**: Single statically linked binary, ultra-low resource footprint. | **Moderate**: Large `node_modules` folders, complex dependency security audits. | **Excellent**: Minimal footprint, slow build/compile times in CI/CD pipeline. | **Moderate**: Requires JVM configuration, higher memory baselines. |

### Decision Rationale: Why C# / .NET 8.0?
* **C# / .NET 8.0** represents the optimal union of performance and developer productivity. It provides native TLS 1.3 support, high-performance custom socket primitives (`System.IO.Pipelines`), and an enterprise-grade ORM (`EF Core 8`).
* **Go** was a strong runner-up for networking, but building complex financial billing and domain logic in Go leads to excessive boilerplate.
* **Node.js** lacks native multi-threaded scaling for thousands of persistent TLS TCP connections and lacks compile-time safety for financial computations.

---

## 2. Architectural Topology Evaluation

### Option A — Modular Monolith [Selected]
The backend is structured as a single process, divided into distinct, loosely-coupled modules with strict boundary separation. Communication across modules is asynchronous via an in-memory event bus or synchronous via interface calls.

* **Operational Complexity**: **Low** - Single process to run, log, monitor, and deploy via Docker Compose in offline-first LAN environments.
* **Transactions & Financial Integrity**: **Excellent** - Simple, database-level ACID transactions across module entities where required.
* **Extraction Path**: **High** - Strict interface communication boundaries make extracting any module into a separate microservice straightforward.

### Option B — Microservices
The backend is split into multiple independently-run services (Workstation Service, Billing Service, Telemetry Service), each with its own database and communication protocol.

* **Tradeoff**: Extremely complex for local LAN gaming center deployments. Introduces distributed transaction overhead (Sagas, 2PC) and high operational friction.

---

## 3. Persistence & Caching Technologies

### Database Engine: PostgreSQL v16+
* **Rationale**: Industry-standard ACID compliance, exact decimal currency representation, and high-performance `JSONB` indexing for semi-structured telemetry data and configuration packages.

### ORM: EF Core 8
* **Rationale**: Rich LINQ querying, compile-time type safety, migration tracking, and unit-testability via in-memory/SQLite mocks.

### Distributed Cache & Messaging: Redis v7+
* **Rationale**: Sub-millisecond latency for session state, rate limiting, fast-path deduplication, and Redis Pub/Sub for real-time command routing across monolith nodes.
