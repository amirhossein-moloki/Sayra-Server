# SAYRA Central Backend Documentation

Welcome to the **SAYRA Central Backend** documentation knowledge base. This documentation set is designed for software engineers, systems administrators, and maintainers to understand the architecture, domain model, security controls, development workflow, and operational requirements of the system.

---

## 📚 Documentation Navigation

### 1. 🏗️ [Architecture](architecture/overview.md)
* **[Architecture Overview](architecture/overview.md)**: System design, Modular Monolith topology, module boundaries, and execution stack.
* **[Architectural Diagrams](architecture/diagrams.md)**: Mermaid C4 diagrams illustrating high-level architecture, module boundaries, and production deployment topologies.
* **[Data Ownership & Scalability](architecture/data-ownership-and-scalability.md)**: Module schema isolation, Redis distributed state, high-volume telemetry ingestion, and financial decimal rules.
* **[Technology Comparison](architecture/technology-comparison.md)**: Engineering rationale for selecting .NET 8, C#, PostgreSQL, EF Core 8, Redis, and Docker Compose.
* **[Architectural Risks](architecture/risks.md)**: Technical risk matrix, client protocol immutability constraints, local Root CA management, and socket buffer memory optimization.

### 2. 💼 [Domain Model & Business Rules](domain/overview.md)
* **[Domain Overview & Invariants](domain/overview.md)**: Core domain concepts across Identity, Workstations, Sessions, Pricing, Billing, Reservations, Telemetry, and Configuration Control Plane. Enforces strict server-authoritative time, financial decimal representation, and database-level idempotency.

### 3. 🔐 [Security & Auditing](security/overview.md)
* **[Security Architecture](security/overview.md)**: Network encryption (TLS 1.3), offline Local Root CA PKI, JWT & RBAC permission controls, resource-level authorization, anti-replay sliding windows, sequence tracking, and append-only hash-chained audit logging with sensitive pattern redaction.

### 4. 🔌 [API & Protocols](api/overview.md)
* **[API & Protocol Specifications](api/overview.md)**: Multi-protocol contracts including REST API specifications, persistent TLS 1.3 TCP Socket messaging framing (`SecureMessageEnvelope`), UDP LAN Server Discovery (Port 37020), and authoritative Configuration Sync API.

### 5. 🛠️ [Development Workflow](development/getting-started.md)
* **[Getting Started & Local Setup](development/getting-started.md)**: Environment setup, running dependencies with Docker Compose, building and executing the Modular Monolith locally.
* **[Testing Strategy](development/testing.md)**: Test execution instructions, unit testing guidelines (`dotnet test tests/Sayra.Backend.UnitTests`), integration test suites, and architecture invariant checks.

### 6. 🚀 [Operations & Deployment](operations/deployment.md)
* **[Deployment & Operational Procedures](operations/deployment.md)**: Production deployment guide using Docker Compose, environment configuration (`ServerOptions`, `SecurityOptions`, `ConfigurationCacheOptions`), background workers (`LivenessMonitoringWorker`, `RemoteCommandTimeoutWorker`), and OpenTelemetry / Serilog observability.

### 7. 📜 [Architectural Decision Records (ADRs)](decisions/index.md)
* **[ADR Index](decisions/index.md)**: Record of 12 foundational engineering decisions:
  - [ADR-001: Language and Runtime (C# / .NET 8.0)](decisions/ADR-001.md)
  - [ADR-002: Backend Framework (ASP.NET Core Web API)](decisions/ADR-002.md)
  - [ADR-003: Architecture Topology (Modular Monolith)](decisions/ADR-003.md)
  - [ADR-004: Database Engine (PostgreSQL v16+)](decisions/ADR-004.md)
  - [ADR-005: Data Access & ORM (EF Core 8)](decisions/ADR-005.md)
  - [ADR-006: Distributed Caching & Messaging (Redis v7+)](decisions/ADR-006.md)
  - [ADR-007: Background Processing (Hosted Services)](decisions/ADR-007.md)
  - [ADR-008: Multi-Protocol Communication Architecture](decisions/ADR-008.md)
  - [ADR-009: Offline Security & Local PKI](decisions/ADR-009.md)
  - [ADR-010: Containerized Deployment (Docker Compose)](decisions/ADR-010.md)
  - [ADR-011: Observability & Telemetry (OpenTelemetry / Serilog)](decisions/ADR-011.md)
  - [ADR-012: Horizontal Scaling Strategy](decisions/ADR-012.md)

---

## 🎯 Document Quality & Maintenance Standards

All documentation in this repository must adhere to the following principles:
1. **Source of Truth Alignment**: In any discrepancy between documentation and implementation, code in `src/` is the authoritative source of truth.
2. **Server Authority**: The backend is server-authoritative in all timing, financial calculations, session states, and configuration resolution.
3. **Immutability & Safety**: No floating-point numbers for monetary values; no cross-module database foreign keys; no raw password or token logging.
