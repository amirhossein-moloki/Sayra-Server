## 2026-03-30 - Static Compiled Regex in Domain Entity Validation

**Learning:** In domain entities like `Workstation.cs`, validation methods (`NormalizeAndValidate`) are invoked frequently during registration, updates, and telemetry handling. Instantiating a new `Regex` instance on every call creates unnecessary heap allocations and Regex parsing overhead, taking ~590ms for 50,000 iterations compared to ~9ms with a `static readonly Regex` using `RegexOptions.Compiled`.
**Action:** When performing string pattern validations in entity or DTO validation pipelines, always use class-level static compiled `Regex` instances (or generated regexes) instead of allocating `new Regex(...)` inside instance methods.
