## 2026-09-10 - Static Pre-compiled Regex for Hot Domain Entity Validation Methods
**Learning:** Instantiating `new Regex(...)` inside frequently-executed domain entity validation methods (like `Workstation.NormalizeAndValidate()`) creates unnecessary per-call heap allocations and forces regex parsing on every call.
**Action:** Extract domain validation regex patterns to `private static readonly Regex` fields initialized with `RegexOptions.Compiled` to ensure zero per-call allocations and optimal execution speed across workstation fleet operations.
