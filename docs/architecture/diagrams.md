# System Diagrams

This document visualizes the high-level system architecture, module interaction rules, and deployment topologies for the **SAYRA Central Backend**.

---

## 1. System Architecture Diagram

```mermaid
graph TD
    subgraph ClientEcosystem [SAYRA Client Fleet & Admin UIs]
        Client[Client Workstation - Desktop Agent]
        AdminUI[Admin Web Dashboard / Operator Portal]
    end

    subgraph EntryPoints [Network Entry Points]
        UDPListener[UDP Broadcast Listener - Port 37020]
        TCPServer[TLS 1.3 TCP Socket Server - Port 37021]
        RESTServer[ASP.NET Core REST API - Port 5001 / 443]
    end

    subgraph Monolith [SAYRA Central Backend - Modular Monolith Process]
        subgraph GatewayHandlers [Communication Handlers]
            UDP_Handler[UDP Discovery Handler]
            TCP_Handler[TCP Frame & Session Handler]
            API_Controllers[REST Controllers & Middleware]
        end

        EventBus[In-Memory EventBus / Dispatcher]

        subgraph CoreModules [Domain Modules]
            Identity[Identity & Access]
            Workstations[Workstation Fleet]
            Sessions[Session Management]
            Billing[Billing & Financial Engine]
            Pricing[Pricing & Rates]
            Reservations[Reservations]
            TelemetryMod[Telemetry & Events]
            ConfigMod[Configuration Control Plane]
        end

        EFCore[EF Core 8 ORM]
        RedisClient[StackExchange.Redis Client]
    end

    subgraph Storage [Persistence & Ephemeral State]
        Postgres[(PostgreSQL v16 Database)]
        Redis[(Redis Distributed Cache & Pub/Sub)]
    end

    Client -.->|UDP Broadcast| UDPListener
    Client ===>|Persistent TCP + TLS 1.3| TCPServer
    Client --->|HTTPS REST API| RESTServer
    AdminUI --->|HTTPS REST API| RESTServer

    UDPListener --> UDP_Handler
    TCPServer --> TCP_Handler
    RESTServer --> API_Controllers

    UDP_Handler --> EventBus
    TCP_Handler --> EventBus
    API_Controllers --> EventBus

    EventBus --> Identity
    EventBus --> Workstations
    EventBus --> Sessions
    EventBus --> Billing
    EventBus --> Pricing
    EventBus --> Reservations
    EventBus --> TelemetryMod
    EventBus --> ConfigMod

    CoreModules --> EFCore
    CoreModules --> RedisClient

    EFCore ===> Postgres
    RedisClient ===> Redis
```

---

## 2. Module Boundaries & Data Isolation

```mermaid
graph LR
    subgraph WorkstationModule [Workstation Module]
        W_Schema[(Workstation Schema)]
        W_Logic[Workstation Aggregates]
        W_API[IWorkstationPublicApi]
    end

    subgraph SessionModule [Session Module]
        S_Schema[(Session Schema)]
        S_Logic[Session Aggregates]
        S_API[ISessionPublicApi]
    end

    subgraph BillingModule [Billing Module]
        B_Schema[(Billing Schema)]
        B_Logic[Financial Transaction Engine]
        B_API[IFinancialAccountService]
    end

    EventBus((Shared In-Memory EventBus))

    SessionModule -->|Calls Public API| W_API
    BillingModule -->|Calls Public API| S_API

    W_Schema -.->|FORBIDDEN DIRECT SQL JOIN| S_Logic
    S_Schema -.->|FORBIDDEN DIRECT SQL JOIN| B_Logic

    W_Logic -->|Publish: WorkstationOnlineEvent| EventBus
    EventBus -->|Consume| S_Logic
    S_Logic -->|Publish: SessionEndedEvent| EventBus
    EventBus -->|Consume| B_Logic
```

---

## 3. Production Deployment Topology

```text
                               ┌─────────────────────────┐
                               │ LAN Gaming Client Fleet │
                               └────────────┬────────────┘
                                            │
               ┌────────────────────────────┼────────────────────────────┐
               │                            │                            │
               ▼ (UDP Discovery)            ▼ (TCP TLS 1.3)              ▼ (HTTPS REST)
     ┌──────────────────┐         ┌──────────────────┐         ┌──────────────────┐
     │ Port 37020 (UDP) │         │ Port 37021 (TCP) │         │ Port 5001 / 443  │
     └─────────┬────────┘         └─────────┬────────┘         └─────────┬────────┘
               │                            │                            │
               └────────────────────────────┼────────────────────────────┘
                                            ▼
                         ┌────────────────────────────────────┐
                         │ NGINX / Keepalived Virtual IP      │
                         │ (TLS 1.3 Passthrough / Proxy)     │
                         └──────────────────┬─────────────────┘
                                            │
                               ┌────────────┴────────────┐
                               ▼                         ▼
                     ┌──────────────────┐      ┌──────────────────┐
                     │ Monolith Node 01 │      │ Monolith Node 02 │
                     │ (Docker Compose) │      │ (Docker Compose) │
                     └─────────┬────────┘      └─────────┬────────┘
                               │                         │
                               └────────────┬────────────┘
                                            │
                               ┌────────────┴────────────┐
                               ▼                         ▼
                     ┌──────────────────┐      ┌──────────────────┐
                     │ Redis v7 Cluster │      │ PostgreSQL v16   │
                     │ (Cache, Pub/Sub, │      │ Primary DB       │
                     │  Session State)  │      │ (ACID Ledger)    │
                     └──────────────────┘      └──────────────────┘
```
