## 2026-09-08 - Reusing Compiled Static Regex in Domain Entity Validation

**Learning:** Instantiating `new Regex(...)` inside domain entity validation methods (like `NormalizeAndValidate()`) causes repetitive Regex parsing, compilation, and heap allocations on every entity validation call.
**Action:** Always define a `private static readonly Regex ... = new(..., RegexOptions.Compiled)` field at class level for regexes used in domain entity validation routines.
