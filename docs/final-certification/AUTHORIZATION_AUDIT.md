# Stage 11-02 — Authorization Audit Report

## 1. Executive Summary

This document presents the security audit findings for the **Authorization Framework** of SAYRA Central Backend. The audit evaluates Role-Based Access Control (RBAC), Permission Enforcement, HTTP API Endpoint Security, TCP Command Authorization, Resource Scope & Ownership Rules, Error Payload Sanitization, and Security Audit Logging.

---

## 2. Role-Based Access Control (RBAC) & Permission Model

### 2.1 Cataloged System Roles
1. **Administrator (`Administrator`)**: Full administrative authority over all organization/site resources, workstations, financial ledgers, system settings, and user management.
2. **Manager (`Manager`)**: Site-level operational control, workstation management, session management, pricing configuration, and financial data reporting.
3. **Operator (`Operator`)**: Day-to-day cafe operational actions including locking/unlocking workstations, starting/stopping sessions, processing payments, and viewing local site status.
4. **Gamer (`Gamer`)**: End-user account with self-service capabilities (viewing personal profile, starting/extending personal sessions, creating personal reservations, making deposits).

### 2.2 Permission Catalog & Mapping Matrix

| Permission Code | Description | Administrator | Manager | Operator | Gamer |
|---|---|:---:|:---:|:---:|:---:|
| `workstations:view` | View workstation fleet status | Yes | Yes | Yes | No |
| `workstations:control` | Issue remote commands (restart, shutdown, etc.) | Yes | Yes | Yes | No |
| `workstations:lock` | Lock workstation screen | Yes | Yes | Yes | No |
| `workstations:unlock` | Unlock workstation screen | Yes | Yes | Yes | No |
| `workstations:manage` | Register/configure workstations | Yes | Yes | No | No |
| `sessions:start` | Start workstation session | Yes | Yes | Yes | Yes (Own) |
| `sessions:stop` | Stop workstation session | Yes | Yes | Yes | Yes (Own) |
| `sessions:pause` | Pause active session | Yes | Yes | Yes | Yes (Own) |
| `sessions:resume` | Resume paused session | Yes | Yes | Yes | Yes (Own) |
| `sessions:extend` | Add time to session | Yes | Yes | Yes | Yes (Own) |
| `sessions:view` | View active/historical sessions | Yes | Yes | Yes | Yes (Own) |
| `reservations:create` | Create workstation reservation | Yes | Yes | Yes | Yes (Own) |
| `reservations:view` | View reservations | Yes | Yes | Yes | Yes (Own) |
| `reservations:manage` | Modify/assign reservations | Yes | Yes | Yes | No |
| `reservations:cancel` | Cancel reservation | Yes | Yes | Yes | Yes (Own) |
| `pricing:view` | View rates and pricing rules | Yes | Yes | Yes | Yes |
| `pricing:manage` | Create/edit pricing rules and plans | Yes | Yes | No | No |
| `financial:view` | View financial accounts & transactions | Yes | Yes | Yes | Yes (Own) |
| `financial:manage` | Debit/credit accounts | Yes | Yes | No | No |
| `financial:process_payment` | Process customer payments | Yes | Yes | Yes | Yes (Own) |
| `financial:view_ledger` | View financial ledger entries | Yes | Yes | Yes | Yes (Own) |
| `users:manage` | Create/edit users, assign roles | Yes | No | No | No |
| `audit:view` | View system audit logs | Yes | Yes | No | No |
| `security_events:view` | View security & forensic events | Yes | Yes | No | No |

---

## 3. Endpoint Authorization & Controller Audit

All 27 API Controllers in `Sayra.Backend.Api` were audited for explicit authentication and permission bindings (`[HasPermission(...)]`).

### 3.1 Endpoint Permission Audit Summary

| Controller | HTTP Methods | Required Permission | Resource Scope Enforcement |
|---|---|---|---|
| `AuthController` | POST | None (Public Login) / Auth (Logout, Me) | Token Revocation & User Context |
| `AccountsController` | GET, POST | `financial:view`, `financial:view_ledger`, `financial:process_payment` | Gamer Ownership / Site Scope |
| `ConfigurationLifecycleController` | GET, POST, PUT | `workstations:view`, `workstations:manage` | Site Scope Isolation |
| `ConfigurationSyncController` | GET, POST | `workstations:view`, `workstations:manage` | Workstation Identity Scope |
| `ConfigurationTargetingController` | GET, POST, DELETE | `workstations:view`, `workstations:manage` | Target Workstation Isolation |
| `GamersController` | GET, POST, PUT | `users:manage` / Auth (Self) | Self Profile Ownership |
| `HealthController` | GET | None (Public Liveness) / Internal | None |
| `MonitoringController` | GET | `workstations:view` | Site Scope Isolation |
| `OfflineDlqController` | GET, POST | `workstations:manage` | Tenant Isolation |
| `OrganizationsController` | GET, POST, PUT | `users:manage` | Organization Boundary |
| `PaymentsController` | GET, POST | `financial:view`, `financial:process_payment` | Gamer Ownership / Site Scope |
| `PermissionsController` | GET, POST | `users:manage` | System-wide RBAC |
| `PricingController` | GET, POST, PUT | `pricing:view`, `pricing:manage` | Site Scope |
| `ReservationsController` | GET, POST, DELETE | `reservations:create`, `reservations:view`, `reservations:manage`, `reservations:cancel` | Gamer Ownership / Site Scope |
| `RolesController` | GET, POST, PUT, DELETE | `users:manage` | System-wide RBAC |
| `SessionsController` | GET, POST, PUT | `sessions:start`, `sessions:stop`, `sessions:pause`, `sessions:resume`, `sessions:extend`, `sessions:view` | Gamer Ownership / Site Scope |
| `SitesController` | GET, POST, PUT | `users:manage` | Organization Scope |
| `TransactionsController` | GET | `financial:view`, `financial:view_ledger` | Gamer Ownership / Site Scope |
| `UpdateArtifactsController` | GET, POST, DELETE | `updates:view`, `updates:manage` | Global / Site Scope |
| `UpdateDownloadController` | GET | `updates:view` | Client Workstation Scope |
| `UpdateManifestController` | GET | `updates:view` | Client Workstation Scope |
| `UpdateOperationsController` | GET, POST | `updates:view`, `updates:manage` | Global Scope |
| `UpdateReleasesController` | GET, POST, PUT | `updates:view`, `updates:manage` | Global Scope |
| `UpdateTargetsController` | GET, POST, DELETE | `updates:view`, `updates:manage` | Site Scope |
| `WorkstationAssignmentsController` | GET, POST | `workstations:view`, `workstations:manage` | Site Scope |
| `WorkstationsController` | GET, POST, PUT, DELETE | `workstations:view`, `workstations:manage`, `workstations:control` | Site Scope / PC-ID Matching |
| `ZonesController` | GET, POST, PUT | `workstations:view`, `workstations:manage` | Site Scope |

---

## 4. TCP Command Authorization Audit

Remote commands issued to client workstations over TCP/TLS undergo multi-tier authorization in `RemoteCommandManager`:

1. **Sender Permission Check**: When requested via API, `CallerPrincipal` permissions are evaluated against command type:
   - `LOCK_WORKSTATION`: Requires `workstations:lock`
   - `UNLOCK_WORKSTATION`: Requires `workstations:unlock`
   - `RESTART_WORKSTATION`, `SHUTDOWN_WORKSTATION`, `LAUNCH_APPLICATION`, `TERMINATE_APPLICATION`: Requires `workstations:control`
2. **Target Workstation Validation**: Target `PcId` verified against PostgreSQL database. Deactivated/disabled workstations return `WORKSTATION_INELIGIBLE`.
3. **Cross-Workstation Command Forgery Protection**: During ACK processing (`ProcessCommandAckAsync`) and Result submission (`ProcessCommandResultAsync`), connection PC-ID is matched against target PC-ID. Unmatched attempts return `CROSS_WORKSTATION_FORGERY` and log a security event.

---

## 5. Resource Ownership & Scope Validation

Resource access in `AuthorizationService.AuthorizeAsync` evaluates:
- **Gamer Ownership**: Non-admin Gamers can only access their own profile, reservations, sessions, and financial accounts. Attempts to access another Gamer's resource return `CROSS_GAMER_ACCESS_DENIED`.
- **Site Scope Isolation**: Non-admin Operators/Managers are bounded by `principal.SiteId`. Accessing resources outside assigned site returns `CROSS_SITE_ACCESS_DENIED`.
- **Organization Scope Isolation**: Accessing resources outside assigned organization returns `CROSS_ORGANIZATION_ACCESS_DENIED`.
- **Device Identity Matching**: Authenticated device `PcId` must match target workstation `PcId`.

---

## 6. Authorization Failure Response Sanitization

When access is denied, `PermissionAuthorizationFilter` and API error handlers produce clean, standardized JSON error payloads:

```json
{
  "code": "FORBIDDEN",
  "message": "Permission 'workstations:manage' is required.",
  "traceId": "0HN123456789A:00000001"
}
```

**Security Guarantees**:
- No internal stack traces, C# exception details, SQL queries, or internal permission rule definitions are leaked to callers.
- Unauthenticated requests return HTTP `401 Unauthorized`.
- Forbidden authenticated requests return HTTP `403 Forbidden`.

---

## 7. Security Audit Logging

All authorization decisions generate audit records:
- **`AuditEvent`**: Persisted in PostgreSQL with `EventType` (`AUTHORIZATION_DENIED`, `RESOURCE_ACCESS_DENIED`, `RESOURCE_ACCESS_GRANTED`), `CorrelationId`, and serialized context JSON.
- **`SecurityEvent`**: Persisted via `ISecurityEventService` with `Who` (Actor ID/Type), `What` (Action/Permission), `When` (Timestamp), `Where` (Site/Org/Device ID), and `Result` (`GRANTED`/`DENIED` with failure reason).

---

## 8. Certification Statement

The Authorization Framework of SAYRA Central Backend has been audited and verified. Fail-closed defaults, strict scope boundaries, cross-workstation forgery prevention, and sanitized failure responses ensure complete access control integrity across all HTTP API endpoints and TCP command channels.
