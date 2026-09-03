# System Architecture Overview

## 1. Architectural Principles & Pattern

The **SAYRA Central Backend** is designed as a **Modular Monolith** adhering to **Clean Architecture** principles. It delivers high-frequency real-time networking, financial billing accuracy, workstation management, and configuration control within a single, highly performant ASP.NET Core process on .NET 8.0.

### Core Architectural Mandates
1. **Module Isolation**: Independent domain modules (e.g., `Identity`, `Workstations`, `Sessions`, `Billing`, `Reservations`, `Telemetry`, `Configuration`) own their data models. Direct cross-module SQL joins or database foreign keys are strictly prohibited.
2. **Server Authority**: The backend is server-authoritative for time (`DateTime.UtcNow`), monetary calculations, session lifecycles, and configuration targeting.
3. **Multi-Protocol Monolith**: A single backend process host concurrently manages:
   * **UDP Discovery Listener (Port 37020)**: Responds to client LAN discovery broadcasts with RSA-signed server transport parameters.
   * **TLS 1.3 Persistent TCP Server (Port 37021)**: High-performance socket server processing framing, HMAC-SHA256 authenticated envelopes, sequence numbers, and heartbeats.
   * **HTTPS REST API (Port 5001 / 443)**: RESTful controllers for management, administrative control, gaming sessions, and configuration synchronization.
4. **Clean Layering**:
   * **Domain Layer (`Sayra.Backend.Domain`)**: Aggregate roots, entities, domain events, value objects, and pure business invariants. No external framework dependencies.
   * **Application Layer (`Sayra.Backend.Application`)**: CQRS command and query handlers, public interfaces, domain event dispatching, and business orchestration.
   * **Infrastructure Layer (`Sayra.Backend.Infrastructure`)**: EF Core 8 mappings, PostgreSQL repositories, Redis caching and distributed state, TCP/UDP socket servers, and OpenTelemetry logging.
   * **API Layer (`Sayra.Backend.Api`)**: Controllers, custom authentication handlers, authorization filters, and middleware.

---

## 2. Module Boundaries & Communication

```text
[ API / Socket Gateways ]
        │
        ▼
[ Application Orchestration Layer ] ◄───► [ Public Interfaces (e.g. IFinancialAccountService) ]
        │
  ┌─────┴─────────────────────────┬─────────────────────────┐
  ▼                               ▼                         ▼
[ Workstations Module ]     [ Sessions Module ]     [ Billing Module ]
  (Workstation DB Schema)    (Session DB Schema)     (Billing DB Schema)
```

### Decoupled Communication Mechanisms
* **In-Memory Event Bus**: Modules publish domain events (e.g., `SessionStartedEvent`, `WorkstationOnlineEvent`). Subscribing handlers in other modules react asynchronously without tight coupling.
* **Public Module Interfaces**: Synchronous read-only queries or cross-module domain interactions are exposed strictly via public application interfaces (e.g., `IFinancialAccountService`, `IWorkstationPublicApi`).
* **Cross-Module References**: Entities reference records in other modules exclusively by stable UUID identifiers (e.g., `WorkstationId`, `GamerId`).

---

## 3. Physical Solution Structure

```text
src/
├── Sayra.Backend.Api/          # REST API endpoints, Auth handlers, Middleware
├── Sayra.Backend.Application/  # CQRS Commands/Queries, Handlers, Services, Schema validation
├── Sayra.Backend.Domain/       # Core Aggregates, Entities, Enums, Value Objects, Domain Events
├── Sayra.Backend.Infrastructure/# PostgreSQL EF Core configurations, Migrations, Redis, Sockets
├── Sayra.Backend.Shared/       # Common utilities, DateTime helpers
├── Sayra.Backend.Contracts/    # DTOs, Serialization models, Message contracts
└── Sayra.Backend.Modules/      # Modular assembly definitions
```
