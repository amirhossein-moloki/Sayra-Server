# Architectural Decision Records (ADRs)

This index summarizes the key architectural and technology decisions locked for the **SAYRA Central Backend**.

---

## 📋 ADR Index

* **[ADR-001: Language and Runtime](ADR-001.md)**: Selection of C# and .NET 8.0 (LTS) for async socket networking, native TLS 1.3, compile-time LINQ, and high development velocity.
* **[ADR-002: Backend Framework](ADR-002.md)**: Selection of ASP.NET Core Web API for unified REST endpoints, hosted background services, and custom TCP/UDP socket servers.
* **[ADR-003: Monolith vs. Microservices](ADR-003.md)**: Selection of a Modular Monolith topology to deliver low operational complexity for offline-first LAN gaming centers while maintaining clear extraction paths.
* **[ADR-004: Database Engine](ADR-004.md)**: Selection of PostgreSQL (v16+) for ACID compliance, exact decimal currency representation, and high-performance JSONB storage.
* **[ADR-005: Data Access & ORM](ADR-005.md)**: Selection of Entity Framework Core 8 (EF Core 8) for LINQ query safety, migration management, and entity mapping.
* **[ADR-006: Distributed Caching & Messaging](ADR-006.md)**: Selection of Redis (v7+) for sub-millisecond session state caching, fast-path deduplication, and Redis Pub/Sub command routing.
* **[ADR-007: Background Processing](ADR-007.md)**: Selection of ASP.NET Core `BackgroundService` hosted workers for recurring maintenance, heartbeat evaluation, and command timeout management.
* **[ADR-008: Multi-Protocol Communication Architecture](ADR-008.md)**: Concurrent hosting of HTTPS REST API (Port 5001), persistent TLS 1.3 TCP Socket Server (Port 37021), and UDP Discovery Server (Port 37020) inside a unified host process.
* **[ADR-009: Offline Security & Local PKI](ADR-009.md)**: Provisioning of a self-managed Local Root Certificate Authority (Local Root CA) for offline TLS 1.3 encryption and HMAC-SHA256 authenticated message envelopes.
* **[ADR-010: Containerized Deployment Architecture](ADR-010.md)**: Multi-platform containerization using Docker Compose for staging and production parity.
* **[ADR-011: Observability & Diagnostics](ADR-011.md)**: Unified structured logging (Serilog), OpenTelemetry metrics, and distributed tracing across HTTP and TCP protocols.
* **[ADR-012: Horizontal Scaling Strategy](ADR-012.md)**: Scaling out via multiple monolith nodes using Redis Pub/Sub command routing and distributed state storage.
