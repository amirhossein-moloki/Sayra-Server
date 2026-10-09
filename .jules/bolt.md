# Bolt's Journal - Critical Learnings

## 2026-03-30 - Initial Setup
**Learning:** Baseline unit test suite (`Sayra.Backend.UnitTests`) contains 794 tests and `Sayra.Backend.ArchitectureTests` contains 3 tests, all passing cleanly. Integration tests require live database/host setup.
**Action:** Always verify optimizations against unit tests (`dotnet test tests/Sayra.Backend.UnitTests/Sayra.Backend.UnitTests.csproj`) and architecture tests.

## 2026-03-30 - Span-based Zero-Allocation TCP Frame Parsing
**Learning:** `TcpFrameParser` byte-by-byte buffer accumulation and intermediate `byte[]` allocation per frame during `Encoding.UTF8.GetString` generates significant GC allocations and CPU overhead on TCP message streams.
**Action:** Use `CollectionsMarshal.AsSpan` and `ReadOnlySpan<byte>` with SIMD-accelerated `IndexOf((byte)'\n')`, `_buffer.AddRange(ReadOnlySpan<byte>)`, zero-allocation `Encoding.UTF8.GetString(frameSpan)` slice decoding, and O(1) `_buffer.Clear()` when full frame is consumed.
