## 2026-03-31 - Cache MAC address Regex as static compiled field
**Learning:** Instantiating `new Regex(...)` inside domain validation methods called during entity normalization (such as `Workstation.NormalizeAndValidate()`) causes unnecessary heap allocations and regex parsing overhead per validation call.
**Action:** Always cache compiled `Regex` instances in `private static readonly` fields (`RegexOptions.Compiled`) on domain entities and value objects.
