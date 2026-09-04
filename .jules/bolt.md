## 2026-09-04 - Static Regex Compilation for MAC Address Validation
**Learning:** Instantiating `new Regex(...)` inside domain entity methods like `Workstation.NormalizeAndValidate()` creates heap allocations and triggers regular expression pattern parsing overhead on every entity validation call.
**Action:** Use `private static readonly Regex MacRegex = new Regex(pattern, RegexOptions.Compiled);` at class scope to reuse compiled regular expressions across entity instances.
