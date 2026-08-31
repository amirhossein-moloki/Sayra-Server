## 2026-08-16 - Cache Compiled Regex in Hot Path Entity Validation
**Learning:** Instantiating `new Regex(...)` inside aggregate entity validation methods like `Workstation.NormalizeAndValidate()` creates unnecessary heap allocations and runtime regex compilation overhead during frequent entity operations (e.g. registration, updates, and liveness synchronization).
**Action:** Always store compiled `Regex` objects in static readonly fields with `RegexOptions.Compiled` when validating strings in core domain models or hot execution paths.
