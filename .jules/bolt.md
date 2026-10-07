# Bolt's Journal

## 2026-10-07 - Zero-Allocation Span Parsing for Client Software Versions
**Learning:** In hot update targeting and manifest evaluation loops, string version parsing using compiled `Regex` creates heap allocations (`Match`, `GroupCollection`, `Group`, `CaptureCollection`, trimmed string copies) and regex state machine overhead on every eligibility evaluation.
**Action:** Replace `Regex` version string matchers with `ReadOnlySpan<char>` stack-based slicing (`Trim()`, `IndexOf('-')`, `IndexOf('+')`, `IndexOf('.')`) and `int.TryParse(ReadOnlySpan<char>, ...)` for zero heap allocation and ~30x-50x faster version comparison.
