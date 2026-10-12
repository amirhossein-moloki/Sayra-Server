# Bolt's Journal - Critical Learnings

## 2026-03-30 - TCP Frame JSON Deserialization & Allocation Hot Path
**Learning:** In `TcpServer` and `TcpAuthenticationService`, deserializing payloads via `telemElem.GetRawText()` allocates intermediate JSON strings on every frame, and instantiating `new JsonSerializerOptions` bypasses `System.Text.Json` metadata caching causing high reflection/GC overhead. Passing `JsonElement` directly into `JsonSerializer.Deserialize` with `ProtocolSerialization.Options` avoids string allocations and reuses cached serializer metadata.
**Action:** Always reuse `ProtocolSerialization.Options` or static options instances for JSON deserialization, and pass `JsonElement` directly into `JsonSerializer.Deserialize<T>(element, options)` instead of calling `GetRawText()`.
