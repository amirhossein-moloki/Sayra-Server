## 2026-10-26 - Pre-compiled Static Regex for Entity Validation Hot Paths
**Learning:** Instantiating `new Regex(...)` inside domain entity normalization/validation methods (such as `Workstation.NormalizeAndValidate()`) causes repetitive heap allocations and Regex parsing overhead on every entity validation call during fleet ingest or registration loops.
**Action:** Always store domain entity Regex patterns as `private static readonly Regex MacRegex = new(@"...", RegexOptions.Compiled);` fields.
