# Bolt's Journal - Critical Learnings

## 2026-09-09 - Domain Entity Regex Pattern Caching
**Learning:** In high-throughput domain validation methods (e.g. `Workstation.NormalizeAndValidate()`), instantiating `new Regex(...)` locally allocates on the heap and recompiles/parses the pattern on every call.
**Action:** Always declare domain validation regexes as `private static readonly Regex` with `RegexOptions.Compiled` at the class level.
