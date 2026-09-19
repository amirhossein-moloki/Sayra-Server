## 2026-09-12 - System.Text.Json in High-Throughput TCP Frame Processing

**Learning:** Creating `new JsonSerializerOptions` or calling `elem.GetRawText()` on `JsonElement` inside high-frequency TCP message processing loops (`ProcessSecureMessageAsync` and `TcpAuthenticationService.AuthenticateAsync`) bypasses `System.Text.Json`'s static reflection metadata cache and allocates intermediate payload strings for every single socket frame. Re-using `ProtocolSerialization.Options` and calling `elem.Deserialize<T>(ProtocolSerialization.Options)` directly on `JsonElement` avoids both options re-reflection and intermediate string allocations.

**Action:** Always check socket frame parsers and message loops for `new JsonSerializerOptions` and `elem.GetRawText()`, replacing them with static options (such as `ProtocolSerialization.Options`) and direct `JsonElement` deserialization.
