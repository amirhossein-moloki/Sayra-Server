## 2026-09-10 - Static Compiled Regex for Workstation Validation
**Learning:** Per-method instantiation of `new Regex(...)` in domain entity validation methods (like `Workstation.NormalizeAndValidate()`) causes repetitive Regex parsing and heap allocations on every entity state transition or normalization call. Using a static compiled `Regex` field matches the domain entity pattern in `UpdateRelease` and `UpdatePackage` and eliminates allocations.
**Action:** Always use static compiled `Regex` fields (or source-generated `GeneratedRegex`) for domain entity validations.
