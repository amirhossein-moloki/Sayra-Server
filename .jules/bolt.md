## 2026-09-09 - Cache Compiled Regex in Workstation Domain Entity
**Learning:** Instantiating `new Regex(...)` inside frequently called entity methods like `Workstation.NormalizeAndValidate()` creates unnecessary heap allocations and runtime regex parsing overhead on every workstation state update or registration.
**Action:** Always extract regular expressions into `private static readonly Regex` fields initialized with `RegexOptions.Compiled` in domain entities and hot paths.
