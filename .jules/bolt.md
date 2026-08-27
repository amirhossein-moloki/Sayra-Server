## 2026-08-24 - Cached Compiled Regex for MAC Address Validation
**Learning:** Instantiating `new Regex(...)` on hot validation paths like `Workstation.NormalizeAndValidate()` creates unnecessary heap allocations and runtime regex parsing overhead. Caching a `static readonly Regex` compiled instance (`RegexOptions.Compiled`) drastically improves execution speed and eliminates allocations per validation call.
**Action:** Always check entity normalization and validation methods for un-cached regex patterns or repeated object allocations and convert them to static compiled instances.
