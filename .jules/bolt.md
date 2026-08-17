## 2026-08-17 - Prefer `FirstOrDefaultAsync` Predicates Over `GetAllAsync` LINQ Scans in Repository Pattern

**Learning:** When using generic repository pattern abstractions (`IRepository<T>`), fetching whole entities via `GetAllAsync()` and filtering in-memory using LINQ-to-Objects loads all table rows into memory, transfers entire table payloads over network, and defeats database indexes (`IX_Workstations_PcId`, `IX_Workstations_MacAddress`, `IX_Sites_SiteId`). Using `FirstOrDefaultAsync(predicate)` pushes the filter down to SQL (`WHERE ... LIMIT 1`), utilizing database indexes for O(1)/O(log N) lookups instead of O(N) full table scans.

**Action:** Always check repository queries for `GetAllAsync()` followed by `.FirstOrDefault()` / `.Where()` and replace them with database-level predicate expressions (`FirstOrDefaultAsync(expression)`) when querying specific entities or records.
