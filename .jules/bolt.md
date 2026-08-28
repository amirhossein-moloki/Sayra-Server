# Bolt's Journal

Critical learnings and findings on performance optimizations in this codebase.

## 2025-05-18 - Avoid Per-Invocation Regex Allocations in Hot Path Validation
**Learning:** Instantiating `new Regex(...)` inside domain entity validation methods like `Workstation.NormalizeAndValidate()` creates unnecessary heap allocations and regex parsing overhead on every workstation heartbeats and validation calls.
**Action:** Always extract static regular expressions into `private static readonly Regex` instances with `RegexOptions.Compiled` when validating high-frequency entities.
