## 2026-09-09 - Reuse Compiled Static Regex for High-Frequency Domain Model Validation

**Learning:** Instantiating `new Regex(...)` inside domain entity validation methods (e.g. `Workstation.NormalizeAndValidate()`) causes redundant Regex pattern parsing and heap allocations on every validation call. Utilizing `private static readonly Regex ... = new(..., RegexOptions.Compiled)` eliminates per-call allocation and compilation overhead.

**Action:** Whenever validating strings with regular expressions in entity or value object methods, use a `private static readonly Regex` field with `RegexOptions.Compiled`.
