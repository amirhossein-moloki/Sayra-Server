# Bolt's Journal - Critical Learnings

## 2026-08-15 - Full Table Scan in Repository Query Handlers
**Learning:** Handlers were using `_repository.GetAllAsync()` to retrieve entire tables into memory before performing `.FirstOrDefault(predicate)` in C#. On growing datasets, this causes O(N) database reads, heavy memory allocations, and network overhead instead of an O(1) indexed SQL query (`WHERE "PcId" = @pcId`).
**Action:** Add `FirstOrDefaultAsync(Expression<Func<T, bool>> predicate, ...)` to `IRepository<T>` and `Repository<T>`, and replace in-memory `GetAllAsync()` filtering with database-level predicate queries in `GetWorkstationByPcIdQueryHandler`, `AuthorizeWorkstationCommandHandler`, and `RegisterWorkstationCommandHandler`.
