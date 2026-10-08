## 2026-10-08 - Static Regex Optimization in Domain Entities
**Learning:** Instantiating `new Regex(...)` inside domain entity validation methods (like `Workstation.NormalizeAndValidate()`) creates unnecessary GC heap allocations and regex compilation overhead per validation call.
**Action:** Always check for `new Regex(...)` inside hot validation/normalization loops or domain methods and hoist them to `private static readonly Regex` with `RegexOptions.Compiled`.
