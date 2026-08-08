# Executive Architecture Decision

This document details the final, locked-in architectural and technology decisions for the **SAYRA Central Backend**. Every decision is evaluated against the existing SAYRA Client's compatibility constraints, long-term scalability, financial correctness, security mandates, and modular evolvability.

---

## 1. Primary Recommendation Summary

```text
Language:              C#
Runtime:               .NET 8.0 (LTS)
Framework:             ASP.NET Core / Web API
Architecture:          Modular Monolith (with strict module boundary enforcement)
Database:              PostgreSQL (v16+)
ORM:                   Entity Framework Core (EF Core 8)
Cache:                 Redis (v7+)
Background Processing: Custom hosted background services backed by PostgreSQL/Redis
Messaging:             In-Memory EventBus with Redis Pub/Sub for multi-instance scaling
API:                   RESTful Web API (JSON, Bearer JWT)
TCP:                   Managed TLS 1.3 persistent TCP (custom .NET SslStream socket layer)
UDP:                   UDP Socket-based LAN Discovery (compliant with client broadcast protocols)
Authentication:        Mock Bearer JWT with fallback to central authentication providers
Authorization:         Claim-Based / Role-Based access control
Observability:         OpenTelemetry (Structured Logs via Serilog, Metrics, and Distributed Tracing)
Containerization:      Docker (Multi-stage, Linux-based lightweight Alpine/Distroless images)
Deployment:            Docker Compose (suitable for Dev, Staging, and LAN gaming center Production environments)
Testing:               xUnit, Moq, FluentAssertions, WebApplicationFactory for high-fidelity Integration Testing
```

---

## 2. Executive Justification: Why This Stack?

The SAYRA ecosystem requires a rare blend of **real-time LAN networking** (UDP Server Discovery, persistent TLS 1.3 TCP tunnels), **strict business/financial transactional integrity** (billing, charging, sessions), **high availability**, and **ease of local/LAN deployment**.

The selection of **C# on .NET 8.0** and an **ASP.NET Core Modular Monolith** represents the optimal architectural sweet spot:

1. **Native High-Performance Networking**: .NET 8.0 provides industry-leading socket performance (`System.Net.Sockets`) and first-class native support for **TLS 1.3** and asynchronous binary stream processing (`System.IO.Pipelines`). This allows us to handle thousands of concurrent persistent client TCP connections efficiently in a single process without sacrificing performance or needing external wrappers.
2. **Unified Execution Environment**: Running REST APIs, the custom TCP server, the UDP discovery listener, and background workers in a single Modular Monolith significantly reduces operational complexity. Gaming centers (LAN environments) can host the entire backend on a single local server, or scale out to multiple nodes behind Docker Compose.
3. **Strict Transactional Boundaries (PostgreSQL + EF Core)**: PostgreSQL provides outstanding transactional integrity (ACID), strong financial correctness support (via the `decimal` type), and excellent JSONB storage capabilities. EF Core 8 delivers top-tier productivity, compile-time query validation, and migration safety while offering clean encapsulation within independent modules.
4. **Resiliency and Scalability**: By using **Redis** as a shared distributed state backplane (for active connection registry, transient session heartbeats, and locks) and **PostgreSQL** as the source of truth, we get the operational simplicity of a Monolith with the horizontal scalability of Microservices.
5. **Path to Microservices**: Modules communicate using an asynchronous **In-Memory EventBus** (which can be backed by Redis Pub/Sub or RabbitMQ in the future). This ensures zero runtime tight-coupling, allowing easy service extraction if the domain requires it.

---

## 3. Core Architectural Rules Locked for Next Stages

1. **No Shared Database Tables**: Each module owns its schema. Cross-module data queries must go through explicit Public Interfaces or Domain Events. No foreign keys are allowed across different module schemas.
2. **Monetary Decimals Only**: Floating-point types (`float`, `double`) are strictly prohibited for monetary calculations. The authoritative monetary type is always `decimal`.
3. **No Client Changes**: The existing compiled SAYRA Client is immutable. The backend must adapt perfectly to the client's UDP discovery packets, TLS 1.3 socket protocols, and REST API formats.
