# Bolt's Journal - Critical Learnings

## 2025-05-18 - Static Compiled Regex for High-Frequency Domain Validations
**Learning:** Instantiating `new Regex(...)` inside domain entity validation methods (e.g. `Workstation.NormalizeAndValidate()`) causes repetitive heap allocations and regex parsing overhead per validation call.
**Action:** Use `private static readonly Regex ... = new Regex(pattern, RegexOptions.Compiled);` for fixed domain validation regexes to eliminate allocation and compilation overhead on hot execution paths.
