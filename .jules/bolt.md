# Bolt's Journal - Critical Learnings

## 2026-09-21 - Reuse Compiled Static Regex for Domain Entity Validation
**Learning:** Instantiating `new Regex(...)` inside frequently executed entity validation methods like `Workstation.NormalizeAndValidate()` creates high heap allocation and parsing/compilation overhead per invocation. Using `private static readonly Regex ... = new(..., RegexOptions.Compiled)` reuses the compiled regex instance safely across threads and eliminates GC pressure during high-throughput workstation ingestion or telemetry processing.
**Action:** When validating entity fields or text patterns in hot paths, always use a `static readonly Regex` compiled instance rather than instantiating `new Regex(...)` inside instance methods.
