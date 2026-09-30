# Bolt's Journal - Critical Learnings

## 2026-09-30 - Cache compiled Regex instances in domain entity validations
**Learning:** Instantiating `new Regex(...)` inside frequently executed domain methods (such as `Workstation.NormalizeAndValidate()`) causes repetitive Regex pattern compilation and heap allocations. Using a `private static readonly Regex` with `RegexOptions.Compiled` reuses the compiled regex instance across invocations.
**Action:** Always check domain model normalization/validation methods for un-cached `Regex` instantiations and convert them to `static readonly` compiled Regex instances.
