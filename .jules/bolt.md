# Bolt's Journal - Performance Insights

## 2026-03-30 - Domain Entity Validation Regex Instantiation
**Learning:** In domain entities like `Workstation`, validation methods called repeatedly during state transitions or telemetry/registration processing can accumulate heap allocation and regex parsing overhead if `new Regex(...)` is instantiated per method call.
**Action:** Always prefer `private static readonly Regex ... = new(@"...", RegexOptions.Compiled);` for regular expressions in domain entity validation routines.
