## 2026-09-10 - Static Compiled Regex for Entity Validation
**Learning:** Instantiating `new Regex(...)` inside frequently-called entity methods like `Workstation.NormalizeAndValidate()` creates unnecessary heap allocations and Regex parsing overhead on every call.
**Action:** Always extract Regex patterns used in domain entities or validators into `private static readonly Regex` fields initialized with `RegexOptions.Compiled`.
