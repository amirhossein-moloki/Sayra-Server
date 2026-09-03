# Operations & Deployment Guide

This document covers production deployment procedures, background workers, configuration settings, and observability for the **SAYRA Central Backend**.

---

## 1. Production Deployment Topology (Docker Compose)

The standard deployment model uses Docker Compose to orchestrate the Central Backend Monolith, PostgreSQL database, and Redis cache.

### Standard `Dockerfile`
The backend process is containerized using a multi-stage Docker build producing a lightweight .NET 8 runtime image:

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY publish/ .
ENTRYPOINT ["dotnet", "Sayra.Backend.Api.dll"]
```

### Production Execution Command
```bash
docker compose -f docker-compose.yml up -d
```

---

## 2. Background Hosted Services

The monolith process hosts several ASP.NET Core `BackgroundService` workers that handle recurring maintenance without blocking REST APIs or TCP socket listeners:

1. **`LivenessMonitoringWorker`**: Periodically checks active TCP connections against `HeartbeatTimeout` and `HeartbeatGracePeriod`. Gracefully disconnects stale client connections and releases session sequence state in Redis.
2. **`RemoteCommandTimeoutWorker`**: Scans queued/executing `RemoteCommand` entities against `DeliveryTimeout` and `ExecutionTimeout`. Transitions expired commands to `DELIVERY_TIMEOUT` or `EXECUTION_TIMEOUT` states and records security audit logs.
3. **`ConfigurationSyncWorker` / Cleanup Workers**: Manages background Redis cache invalidation and scheduled database maintenance tasks.

---

## 3. Observability & Monitoring

### 3.1. Structured Logging (Serilog)
* All application and security logs are emitted as structured JSON strings with dynamic property context (`PcId`, `SessionId`, `GamerId`, `CorrelationId`).
* Log output automatically redacts sensitive properties (passwords, tokens, private keys) using `ISecurityEventService`.

### 3.2. Metrics & Distributed Tracing (OpenTelemetry)
* **Meter Name**: `Sayra.Backend.Configuration`
  * Instruments counters: `configuration_fetch_total`, `configuration_sync_request_total`, `configuration_publish_total`, `configuration_rollback_total`, `configuration_cache_hit`, `configuration_cache_miss`, `configuration_validation_failure`.
* **ActivitySource**: `Sayra.Backend.Configuration`
  * Exports OpenTelemetry trace spans across configuration resolution, synchronization, and signing operations.

---

## 4. Health Checks
The backend exposes health checks at `/health`:
* **PostgreSQL Check**: Verifies database connection and migration status.
* **Redis Check**: Verifies Redis cache ping responsiveness.
* **TCP Server Check (`TcpServerHealthCheck`)**: Reports TCP listener operational status (`IsListening`) and active connection count.
