## 2026-08-29 - Use indexed database predicates instead of GetAllAsync in domain validation services
**Learning:** Calling `GetAllAsync()` in domain validation services loads all historical database table rows into memory before applying LINQ-to-Objects filtering, causing O(N) memory allocations and full table scans as entity tables grow.
**Action:** Use `IRepository<T>.FindAsync(predicate)` to push filtering expressions down to EF Core and PostgreSQL indexed B-tree lookups.
