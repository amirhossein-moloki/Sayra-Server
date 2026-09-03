
## 2025-05-18 - JSON Frame Deserialization Heap Optimization in TcpServer
**Learning:** Re-instantiating `JsonSerializerOptions` on every message frame in `System.Text.Json` prevents metadata reflection caching, causing unnecessary GC pressure and CPU cycles. Furthermore, calling `JsonElement.GetRawText()` creates an intermediate heap string allocation before deserialization.
**Action:** Use static cached `JsonSerializerOptions` (such as `ProtocolSerialization.Options`) and call `JsonElement.Deserialize<T>(options)` directly on the `JsonElement`.
