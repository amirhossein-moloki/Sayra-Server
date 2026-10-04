## 2026-10-04 - Static Compiled Regex for Workstation MAC Validation
**Learning:** Instantiating `new Regex(...)` inside domain entity validation methods called during registration/normalization causes unnecessary heap allocations and pattern re-parsing on every call.
**Action:** Use `private static readonly Regex ... = new Regex(..., RegexOptions.Compiled)` for domain entity regex validations.
