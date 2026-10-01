# Bolt's Journal - Performance Learnings

## 2026-09-10 - Zero-Allocation HTTP Range Header Parsing
**Learning:** `UpdateDownloadRangeHelper.ParseRangeHeader` is invoked on chunked update download resume requests. Using `string.Substring`, `Trim()`, and `string.Split`/`IndexOf` creates multiple string allocations on every HTTP range check.
**Action:** Use `ReadOnlySpan<char>` slicing and span-based `long.TryParse(ReadOnlySpan<char>, ...)` to eliminate intermediate heap allocations while preserving exact RFC 7233 range parsing logic.
