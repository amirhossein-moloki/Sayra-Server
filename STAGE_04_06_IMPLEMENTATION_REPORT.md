# STAGE 04-06 Implementation Report: Resource-Level Authorization & Access Policy System

## 1. Authorization Architecture
The resource-level authorization system extends the Phase 04-05 RBAC infrastructure by layering resource context evaluation on top of role/permission checks:
1. **Authentication Check**: Verifies that `UserPrincipal.IsAuthenticated` is true and `UserPrincipal.AccountStatus` is `Active`.
2. **RBAC Permission Check**: Confirms the caller has the required `PermissionCatalog` permission (or `Administrator` role).
3. **Explicit Policy Check (`UserResourceAccess`)**: Evaluates explicit resource access grants/restrictions assigned directly to the user (`UserEntityId`) or any of their active roles (`RoleId`). Explicit denials take precedence over grants; explicit grants override default organizational and site boundary restrictions.
4. **Resource Context Policies**: Evaluates domain-specific boundary rules (Organization, Site, Gamer Ownership, Device Identity) for the specific target resource entity.
5. **Audit Event Generation**: Records audit logs (`RESOURCE_ACCESS_GRANTED`, `RESOURCE_ACCESS_DENIED`, `RESOURCE_SCOPE_CHANGED`, `USER_RESOURCE_ACCESS_REVOKED`).

## 2. Resource Hierarchy
The system enforces resource hierarchy policies across existing Phase 03 domain aggregates:
`Organization` -> `Site` -> `Zone` -> `Workstation` -> `Session` / `Reservation` -> `Financial Data` (`GamerAccount`, `FinancialTransaction`, `Payment`).

## 3. Policies Implemented
- **Organization Policy**: Restricts managers/operators to their assigned `OrganizationEntityId`. Cross-organization access is rejected with `CROSS_ORGANIZATION_ACCESS_DENIED`.
- **Site Policy**: Restricts managers/operators to their assigned `SiteEntityId`. Cross-site access is rejected with `CROSS_SITE_ACCESS_DENIED`.
- **Workstation Policy**: Verifies workstation ownership, site affiliation, and device identity matching (`PcId`).
- **Session Policy**: Enforces gamer ownership for gamers (`CROSS_GAMER_ACCESS_DENIED`) and site/organization boundaries for managers/operators.
- **Reservation Policy**: Enforces gamer ownership and site/organization boundary isolation.
- **Financial Data Policy**: Requires `VIEW_FINANCIAL_DATA` / `MANAGE_FINANCIAL_DATA` permissions and respects gamer ownership and site boundaries.

## 4. Database Changes
- Created `UserResourceAccess` domain entity in `Sayra.Backend.Domain.Entities`.
- Configured EF Core mapping in `UserResourceAccessConfiguration` mapping to table `user_resource_accesses`.
- Registered `DbSet<UserResourceAccess>` on `ApplicationDbContext`.
- Added migration `20260824000000_AddUserResourceAccessTable` with indexes:
  - `IX_UserResourceAccesses_User_Type_Resource` on `(UserEntityId, ResourceType, ResourceId)`
  - `IX_UserResourceAccesses_Role_Type_Resource` on `(RoleId, ResourceType, ResourceId)`

## 5. API Changes
- `POST /api/auth/resource-check`: Evaluates permission and resource access for authenticated users (`ResourceAccessCheckRequestDto` -> `ResourceAccessCheckResponseDto`).
- `GET /api/sites/accessible`: Returns a list of sites accessible to the caller based on their site/org boundary and permissions.
- `GET /api/workstations/accessible`: Returns a list of workstations accessible to the caller.

## 6. TCP Impact
- No changes to raw transport protocols or frame definitions.
- TCP commands (`SESSION_COMMAND_REQUEST`) pass through `IAuthorizationService.AuthorizeAsync` to validate workstation and session resource policies before command execution.

## 7. Security Decisions & Verification
- **Fail Closed**: Any unauthenticated request or disabled account immediately fails authorization.
- **Explicit Deny Priority**: Explicit `IsGranted = false` rules override role-based or implicit grants.
- **Permission & Scope Dual Validation**: Users with resource access grants but missing permissions are denied (`PERMISSION_DENIED`). Users with permissions but accessing restricted resources are denied (`EXPLICIT_RESTRICTION_DENIED`).
- **Boundary Enforcement**: Cross-organization, cross-site, and cross-gamer resource requests return HTTP 403 (`CROSS_ORGANIZATION_ACCESS_DENIED`, `CROSS_SITE_ACCESS_DENIED`, `CROSS_GAMER_ACCESS_DENIED`).
- **Client Non-Trust**: Caller inputs (`SiteId`, `GamerId`) are validated against server-authoritative `UserPrincipal` claims and database entities.

## 8. Concurrency Verification
- DB lookups for `UserResourceAccess` and `UserRoleEntity` use `FindAsync` with `track: false` to avoidEF Core context tracking issues during concurrent checks.
- Access revocation transitions records to `Status = "Disabled"` inside EF Core transactions via `IUnitOfWork.ExecuteInTransactionAsync`.
- Immediate revocation ensures subsequent authorization queries reflect active state changes without stale cached decisions.

## 9. Final Test Results
- Executed unit test suite via `dotnet test tests/Sayra.Backend.UnitTests`: **162/162 Passed**.

## 10. Migration Verification Status
- Migration `20260824000000_AddUserResourceAccessTable` verified against `ApplicationDbContextModelSnapshot`.
- Foreign keys set to `ReferentialAction.Cascade` for clean cleanup on user/role deletion.
- Schema verified with indexes on `(UserEntityId, ResourceType, ResourceId)` and `(RoleId, ResourceType, ResourceId)`.

## 11. Known Limitations
- Redis caching of authorization decisions is not active in this stage; authorization checks query PostgreSQL directly to maintain source of truth consistency.

## 12. Acceptance Matrix
- `[x]` Resource authorization implemented
- `[x]` RBAC integration completed
- `[x]` Organization boundary enforced
- `[x]` Site boundary enforced
- `[x]` Workstation access controlled
- `[x]` Session authorization controlled
- `[x]` Reservation authorization controlled
- `[x]` Financial resource protection implemented
- `[x]` No controller-level authorization duplication
- `[x]` Client cannot bypass authorization
- `[x]` Database constraints verified
- `[x]` EF migrations verified
- `[x]` Unit tests pass
- `[x]` No later-phase features implemented
- `[x]` STAGE_04_06_IMPLEMENTATION_REPORT.md created
