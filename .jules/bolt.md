## 2026-03-31 - Static Compiled Regex for Domain Entity Validation
**Learning:** Instantiating `new Regex(...)` inside domain validation methods (e.g. `Workstation.NormalizeAndValidate()`) called on workstation ingestion/normalization causes repetitive Regex parsing overhead and heap allocations.
**Action:** Use static compiled `Regex` fields (`RegexOptions.Compiled`) for fixed validation patterns in domain entities to eliminate per-validation heap allocations and speed up execution.
