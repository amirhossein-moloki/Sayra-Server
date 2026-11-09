## 2026-09-12 - Static Compiled Regex Allocation Optimization in Domain Validation

**Learning:** In domain entities like `Workstation.NormalizeAndValidate()`, instantiating `new Regex(...)` inside validation methods causes unnecessary heap allocations and regex engine parsing on every call. Using a `private static readonly Regex` with `RegexOptions.Compiled` eliminates per-validation heap allocations and improves throughput during high-frequency workstation registration, heartbeats, and telemetry processing.
**Action:** Always declare regex instances as static readonly compiled fields rather than local variables in hot validation and parsing code paths.
