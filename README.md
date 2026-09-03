# SAYRA Central Backend

[![Build & Test Status](https://img.shields.io/badge/build-passing-brightgreen)](#-testing)
[![Runtime](https://img.shields.io/badge/runtime-.NET%208.0%20LTS-blue)](#-technology-stack)
[![Architecture](https://img.shields.io/badge/architecture-Modular%20Monolith-orange)](#-architecture-overview)

The **SAYRA Central Backend** is a high-performance, server-authoritative backend platform engineered specifically for local LAN gaming centers, client workstation orchestration, real-time telemetry ingestion, prepaid/postpaid billing, and centralized configuration control.

---

## ⚡ Quick Start (Local Development)

### Prerequisites
* [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
* [Docker Desktop](https://www.docker.com/products/docker-desktop/) or Docker Engine with Docker Compose

### 1. Start Infrastructure Services
Start PostgreSQL v16 and Redis v7 containers in the background:
```bash
docker compose up -d
```

### 2. Run the Central Backend Monolith
Launch the ASP.NET Core API server:
```bash
dotnet run --project src/Sayra.Backend.Api/Sayra.Backend.Api.csproj
```
The server will automatically bind and initialize:
* **REST API & Swagger UI**: `http://localhost:5001/swagger`
* **TLS 1.3 TCP Socket Listener**: `Port 37021`
* **UDP LAN Discovery Server**: `Port 37020`

### 3. Run the Unit Test Suite
Execute the fast unit test suite (370 tests):
```bash
dotnet test tests/Sayra.Backend.UnitTests/Sayra.Backend.UnitTests.csproj
```

---

## 🏗️ Architecture Overview

SAYRA Backend is structured as a **Modular Monolith** adhering to Clean Architecture principles:

```text
Sayra.Backend.slnx
├── src/
│   ├── Sayra.Backend.Api/           # REST Controllers, Middleware, Auth Handlers
│   ├── Sayra.Backend.Application/   # Application Core (CQRS, Handlers, Services)
│   ├── Sayra.Backend.Domain/        # Domain Entities, Value Objects, Aggregates, Rules
│   ├── Sayra.Backend.Infrastructure/ # Persistence (EF Core, Postgres), Redis, TCP/UDP
│   ├── Sayra.Backend.Shared/        # Cross-cutting Utilities
│   ├── Sayra.Backend.Contracts/     # Protocol DTOs and Contracts
│   └── Sayra.Backend.Modules/       # Isolated Module Boundaries
└── tests/
    ├── Sayra.Backend.UnitTests/         # Fast Domain & Application Unit Tests
    ├── Sayra.Backend.IntegrationTests/  # Infrastructure Integration Tests
    └── Sayra.Backend.ArchitectureTests/ # ArchUnit Boundary Enforcement
```

### Key Engineering Guarantees
1. **Strict Module Isolation**: Direct cross-module SQL joins and database foreign keys are strictly prohibited. Modules communicate asynchronously via Domain Events or synchronously via Public Interfaces.
2. **Exact Financial Precision**: All monetary values are represented strictly using the `.NET` `decimal` type (`NUMERIC(18,4)` in PostgreSQL). Floating-point arithmetic (`double`, `float`) is strictly forbidden.
3. **Server Authority**: The backend is server-authoritative for session management, billing calculations, server timing (`DateTime.UtcNow`), and effective configuration resolution.
4. **Offline-First Security**: Operates autonomously in local LAN environments using an internal Local Root Certificate Authority (Local Root CA) for TLS 1.3, custom HMAC-SHA256 message envelopes, sliding window replay protection, and sequence number verification.

---

## 📖 Complete Documentation Knowledge Base

Full developer documentation, architectural specifications, security models, and decision records are located in the [`docs/`](docs/README.md) directory:

* **[Documentation Index](docs/README.md)**
* **[Architecture Documentation](docs/architecture/overview.md)**
* **[Domain Model & Business Rules](docs/domain/overview.md)**
* **[Security & Auditing Architecture](docs/security/overview.md)**
* **[API & Protocol Specifications](docs/api/overview.md)**
* **[Development Setup & Workflow](docs/development/getting-started.md)**
* **[Testing Strategy Guide](docs/development/testing.md)**
* **[Operations & Deployment Guide](docs/operations/deployment.md)**
* **[Architectural Decision Records (ADRs)](docs/decisions/index.md)**
