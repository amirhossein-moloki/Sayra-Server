## 2026-09-27 - Static Compiled Regex for Workstation Validation
**Learning:** Instantiating `new Regex(...)` inside domain entity validation methods like `Workstation.NormalizeAndValidate()` creates unnecessary heap allocations and Regex compilation overhead on hot execution paths.
**Action:** Use `private static readonly Regex` fields with `RegexOptions.Compiled` for invariant regular expression patterns within domain entities and validation services.
