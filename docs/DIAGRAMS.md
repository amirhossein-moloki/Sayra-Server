# Architectural & Deployment Diagrams

This document visualizes the high-level system architecture, module boundaries, and deployment topologies for the **SAYRA Central Backend**.

---

## 1. High-Level System Architecture Diagram

This diagram shows the complete message flow from the immutable SAYRA Client to the Central Backend and its underlying data/infrastructure layers.

```mermaid
graph TD
    %% Clients
    subgraph ClientLayer [SAYRA Client Ecosystem]
        Client[SAYRA Client - Immutable Client-Side Code]
    end

    %% Network Entry Points
    subgraph EntryPoints [Network Entry Points]
        UDPListener[UDP Broadcast Listener - Port 37020]
        TCPServer[TCP Socket Server - TLS 1.3 - Port 37021]
        RESTServer[ASP.NET Core REST API - TLS 1.3 - Port 443]
    end

    %% Central Backend process boundary
    subgraph Monolith [SAYRA Central Backend - Modular Monolith Process]
        %% Framework Layer
        subgraph GatewayLayer [Communication Gateways]
            UDP_Handler[UDP Discovery Handler]
            TCP_Handler[TCP Connection Handler]
            API_Controllers[ASP.NET Core Controllers]
        end

        %% Message Bus / Orchestrator
        EventBus[In-Memory EventBus / Command Dispatcher]

        %% Domain Modules
        subgraph DomainModules [Core Domain Modules]
            Identity[Identity & Access]
            Workstations[Workstation Management]
            Sessions[Session Management]
            Billing[Billing & Finance]
            Reservations[Reservation Management]
            TelemetryMod[Telemetry Processing]
        end

        %% Core Infra Gateways
        EFCore[EF Core 8 ORM]
        RedisClient[Redis StackExchange Client]
    end

    %% Infrastructure
    subgraph StorageLayer [Infrastructure / Persistence]
        Postgres[(PostgreSQL v16 Database)]
        Redis[(Redis Cache & Pub/Sub)]
    end

    %% Connections
    Client -.->|UDP Broadcast| UDPListener
    Client ===>|Persistent TCP + TLS 1.3| TCPServer
    Client --->|HTTPS REST APIs| RESTServer

    UDPListener --> UDP_Handler
    TCPServer --> TCP_Handler
    RESTServer --> API_Controllers

    UDP_Handler -->|Pub/Sub Event| EventBus
    TCP_Handler -->|Ingested Event| EventBus
    API_Controllers -->|Dispatch Command| EventBus

    EventBus --> Identity
    EventBus --> Workstations
    EventBus --> Sessions
    EventBus --> Billing
    EventBus --> Reservations
    EventBus --> TelemetryMod

    DomainModules --> EFCore
    DomainModules --> RedisClient

    EFCore ===> Postgres
    RedisClient ===> Redis
```

---

## 2. Module Boundary Diagram

This diagram outlines how independent modules reside in the single monolith codebase, their strict public interfaces, and forbidden dependencies.

```mermaid
graph LR
    subgraph ModuleBoundary [Monolith Module Boundaries]
        subgraph WorkstationModule [Workstation Module]
            W_DB[(Workstation Schema)]
            W_Logic[Workstation Logic]
            W_API[Workstation API/Public Interface]
        end

        subgraph SessionModule [Session Module]
            S_DB[(Session Schema)]
            S_Logic[Session Logic]
            S_API[Session API/Public Interface]
        end

        subgraph BillingModule [Billing Module]
            B_DB[(Billing Schema)]
            B_Logic[Billing Logic]
            B_API[Billing API/Public Interface]
        end
    end

    %% Event Bus Intermediary
    SharedEventBus((Asynchronous EventBus))

    %% Public interfaces dependencies only
    SessionModule -->|Calls Public Interface| W_API
    BillingModule -->|Calls Public Interface| S_API

    %% Forbidden direct DB access
    W_DB -.->|FORBIDDEN DIRECT ACCESS| S_Logic
    S_DB -.->|FORBIDDEN DIRECT ACCESS| B_Logic

    %% Event-based communication
    W_Logic -->|Publish: WorkstationOnline| SharedEventBus
    SharedEventBus -->|Consume| S_Logic
    S_Logic -->|Publish: SessionEnded| SharedEventBus
    SharedEventBus -->|Consume| B_Logic
```

* **Core Rule**: Modules are highly decoupled. Direct cross-module database tables referencing or foreign keys are forbidden. Communication happens either asynchronously via the `SharedEventBus` or synchronously via registered `Public Interfaces` (e.g., `ISessionModuleApi`).

---

## 3. Deployment Topology Diagrams

### 3.1. Development Environment Topology
A clean, minimal, single-machine developer setup running on Windows/macOS/Linux.

```text
[ Developer Machine ]
        |
        +-- Docker Desktop / Local Runtimes
              |
              +-- [App Process] SAYRA Central Backend (Self-Hosted Console App)
              |         |
              |         +---> Ports: 37020 (UDP), 37021 (TCP), 5001 (HTTPS API)
              |
              +-- [Container] PostgreSQL v16 (Local DB port 5432)
              +-- [Container] Redis (Local Cache port 6379)
```

---

### 3.2. Staging / LAN Gaming Center Production Topology
A production-grade, highly available, and redundant setup optimized for high-performance LAN centers.

```text
                         [ LAN Gaming Client Fleet ]
                                      |
                      +---------------+---------------+
                      |               |               |
                      v (UDP Discovery)  v (TCP Persistent)  v (REST/HTTPS API)
             +-----------------+---------------+---------------+
             |                                                 |
             v                                                 v
   [ LAN Router / Switch ]                           [ Keepalived Virtual IP ]
             |                                                 |
             +-------------------------+-----------------------+
                                       |
                                       v
                     [ High-Performance NGINX Load Balancer ]
                     |      (TLS 1.3 Termination for HTTPS)  |
                     |      (TCP streams pass-through)       |
                     +-----------------+---------------------+
                                       |
                     +-----------------+-----------------+
                     |                                   | (Active/Active Load Balancing)
                     v                                   v
        [ Monolith Node 01 ]                [ Monolith Node 02 ]
         (Docker Container)                  (Docker Container)
         - Port 37020 (UDP)                  - Port 37020 (UDP)
         - Port 37021 (TCP)                  - Port 37021 (TCP)
         - Port 5001 (REST)                  - Port 5001 (REST)
                     |                                   |
                     +-----------------+-----------------+
                                       |
                     +-----------------+-----------------+
                     |                                   |
                     v                                   v
             [ Redis Cluster ]                 [ PostgreSQL Cluster ]
             - Distributed state store         - Primary DB (Active Writes)
             - Command pub/sub hub             - Replica DB (Read-Only)
             - Session lock manager            - Streaming replication
```
