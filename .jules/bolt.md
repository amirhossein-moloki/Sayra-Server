## 2026-09-12 - Zero-Allocation Version Slicing vs Regex Match

**Learning:** Replacing `Regex` with stack-allocated `ReadOnlySpan<char>` slicing and `int.TryParse(ReadOnlySpan<char>, ...)` during high-frequency parsing (such as client software version checks in update/manifest workflows) completely eliminates heap allocations (`Match`, `GroupCollection`, `Group`, `CaptureCollection`, string slices) without requiring external dependencies or breaking existing contract behavior.

**Action:** Prefer `ReadOnlySpan<char>` memory slicing over `Regex` for structural string parsing in high-frequency backend endpoints.
