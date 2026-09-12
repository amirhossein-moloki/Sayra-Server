## 2026-09-12 - Static Compiled Regex for Domain Entity Validation

**Learning:** Instantiating `new Regex(...)` inside hot domain entity validation methods (such as `Workstation.NormalizeAndValidate`) causes per-invocation regex pattern parsing and unnecessary heap allocation on every workstation registration or state update. Using a `private static readonly Regex` field with `RegexOptions.Compiled` caches the compiled regex state across all calls, eliminating redundant allocations and CPU overhead during high-throughput workstation validation.
**Action:** Ensure domain entity normalization and validation methods use `private static readonly Regex` with `RegexOptions.Compiled` instead of method-local `new Regex(...)` instances.
