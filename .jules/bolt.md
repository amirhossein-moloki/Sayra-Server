## 2026-09-10 - Static Compiled Regex for Domain Entity Validation
**Learning:** Instantiating `new Regex(...)` inside frequently called domain validation methods like `Workstation.NormalizeAndValidate()` creates unnecessary heap allocations and regex compilation overhead on every domain validation call.
**Action:** Always use `private static readonly Regex` fields with `RegexOptions.Compiled` (or `[GeneratedRegex]`) for regular expressions in domain entity normalization and validation routines.
