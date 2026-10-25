## 2026-09-12 - Compiled Static Regex for Domain Validation

**Learning:** In domain entity validation methods like `Workstation.NormalizeAndValidate()`, instantiating local `new Regex(...)` instances causes per-call heap allocation, string parsing, and regex state machine compilation. Using a `private static readonly Regex` field with `RegexOptions.Compiled` eliminates allocation and parsing overhead on high-frequency validation calls.

**Action:** Whenever validating formats (such as MAC addresses, hostnames, IP patterns, or custom regexes) in entity or model validation methods, ensure the `Regex` is declared as a static compiled field rather than instantiated locally inside the method.
