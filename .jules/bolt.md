## 2026-10-05 - Static Regex Compilation for Entity Validation

**Learning:** Instantiating `new Regex(...)` inside frequently-called entity normalization/validation methods (like `Workstation.NormalizeAndValidate`) introduces unnecessary per-call heap allocations and Regex parsing overhead.
**Action:** Use a `private static readonly Regex` field initialized with `RegexOptions.Compiled` for domain validation regex patterns.
