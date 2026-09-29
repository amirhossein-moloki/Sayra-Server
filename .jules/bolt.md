## 2026-09-30 - Static Compiled Regex in Domain Model Validation
**Learning:** Instantiating `new Regex(...)` inside domain entity validation methods (`NormalizeAndValidate()`) causes unnecessary per-invocation heap allocations and regex pattern parsing overhead on hot paths like workstation ingestion and heartbeats.
**Action:** Always define regular expressions as `private static readonly Regex` fields with `RegexOptions.Compiled` when used in entity validation or frequently called methods.
