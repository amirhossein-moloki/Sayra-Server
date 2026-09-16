## 2026-09-12 - Static Compiled Regex in Domain Validation

**Learning:** Instantiating `new Regex(...)` inside domain validation methods called frequently during ingestion or persistence (e.g. `Workstation.NormalizeAndValidate()`) causes repetitive heap allocations and Regex pattern parsing on every call. Using a `private static readonly Regex` with `RegexOptions.Compiled` eliminates these allocations and improves execution speed.
**Action:** When validating string patterns in high-frequency domain models or services, always declare compiled static Regex fields rather than instantiating local `Regex` objects per invocation.
