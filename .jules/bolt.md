## 2026-09-11 - Static Compiled Regex in Domain Normalization
**Learning:** Instantiating `new Regex(...)` inside domain entity validation methods (e.g. `Workstation.NormalizeAndValidate()`) causes heap allocation and regex compilation overhead on every workstation state update or normalization call.
**Action:** Always define static compiled `Regex` fields (`private static readonly Regex ... = new(..., RegexOptions.Compiled);`) for regular expressions in domain entities and validation services to ensure zero regex allocation on repeated method invocations.
