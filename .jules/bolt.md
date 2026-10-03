## 2026-10-03 - Reuse Compiled Regex Instances for Domain Validation

**Learning:** Instantiating `new Regex(...)` inside hot path domain model validation methods (e.g. `Workstation.NormalizeAndValidate()`) creates unnecessary Regex objects, internal state machines, and heap allocations on every call.
**Action:** Use `private static readonly Regex Name = new(..., RegexOptions.Compiled)` for pattern checks in domain validation routines to achieve zero per-call heap allocation and optimal regex matching performance.
