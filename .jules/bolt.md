# Bolt's Journal

## 2026-09-12 - Reusable Compiled Regex in Domain Entity Validation
**Learning:** Instantiating `new Regex(...)` inside domain entity validation methods (such as `Workstation.NormalizeAndValidate()`) causes repetitive heap allocations and expression parsing overhead during high-frequency ingestion or batch fleet updates. In .NET, defining a `static readonly Regex` with `RegexOptions.Compiled` caches the parsed state machine and eliminates heap churn entirely on repeated invocations.
**Action:** Always verify domain model validation methods for locally instantiated `Regex` instances and replace them with `static readonly Regex` with `RegexOptions.Compiled`.
