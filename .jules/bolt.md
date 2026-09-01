## 2026-09-01 - [Cache Compiled Regex for Workstation Validation]
**Learning:** Instantiating `new Regex(...)` inside frequently-called entity methods like `Workstation.NormalizeAndValidate()` creates unnecessary heap allocations and runtime regex parsing overhead on every call.
**Action:** Always extract regular expressions that are repeatedly evaluated into static `readonly` instances with `RegexOptions.Compiled` to enable zero-allocation execution and faster pattern matching.
