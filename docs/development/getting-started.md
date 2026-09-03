# Developer Getting Started & Local Setup Guide

This guide assists new developers in setting up their local development environment, running infrastructure dependencies, and launching the **SAYRA Central Backend**.

---

## 1. Prerequisites

Ensure your development workstation has the following installed:
* **.NET 8.0 SDK** (LTS): [Download .NET 8.0](https://dotnet.microsoft.com/download/dotnet/8.0)
* **Docker Desktop** (or Docker Engine with Docker Compose): [Download Docker](https://www.docker.com/products/docker-desktop/)
* **Git**: [Download Git](https://git-scm.com/)
* *(Optional)* **PostgreSQL Client (psql)** or DBeaver for database inspection.

---

## 2. Infrastructure Setup (Docker Compose)

The project includes a `docker-compose.yml` file to run local infrastructure dependencies:
* **PostgreSQL v16**: Database instance running on `localhost:5432` (`Database: sayra_db`, `User: sayra_user`, `Password: sayra_password`).
* **Redis v7**: Distributed cache and state store running on `localhost:6379`.

### Start Dependencies
In the root repository directory, run:
```bash
docker compose up -d
```

Verify that both containers are healthy:
```bash
docker compose ps
```

---

## 3. Configuration & Environment Variables

Default environment settings for development are located in `src/Sayra.Backend.Api/appsettings.json` and `.env.example`.

### Key Configuration Sections
* `ConnectionStrings`:
  * `Postgres`: `Host=localhost;Port=5432;Database=sayra_db;Username=sayra_user;Password=sayra_password`
  * `Redis`: `localhost:6379,abortConnect=false`
* `ServerOptions`: Configures TCP/TLS ports (`37021`), UDP discovery port (`37020`), backlog, handshake timeouts (`10s`), and maximum message size limits (`10MB`).
* `SecurityOptions`: Configures Argon2id password hashing parameters (`DegreeOfParallelism=8`, `MemorySizeKb=65536`, `Iterations=3`).
* `ConfigurationCacheOptions`: Configures Redis cache key prefixing (`sayra:config:v1:`) and TTLs.

---

## 4. Running the Central Backend

Launch the backend monolith using the .NET CLI:

```bash
dotnet run --project src/Sayra.Backend.Api/Sayra.Backend.Api.csproj
```

Upon successful startup, console output will confirm active listeners:
```text
[INFO] Application starting...
[INFO] Local Root CA initialized. Server certificate provisioned.
[INFO] UDP Discovery Server listening on 0.0.0.0:37020
[INFO] TLS 1.3 TCP Server listening on 0.0.0.0:37021
[INFO] Now listening on: http://localhost:5001
```

### Accessing Swagger UI
Open your browser and navigate to:
`http://localhost:5001/swagger`
