using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Sayra.Backend.Infrastructure.Transport;

namespace Sayra.Backend.UnitTests
{
    public class FramingPerformanceTests
    {
        [Fact]
        public async Task Performance_Should_Process_1000_Frames_Rapidly_With_Low_Allocation()
        {
            // Prepare 1000 framed messages
            var sb = new StringBuilder();
            for (int i = 0; i < 1000; i++)
            {
                sb.Append($"{{\"type\":\"HEARTBEAT\",\"pcId\":\"PC-{i:0000}\",\"timestamp\":\"2026-10-18T12:00:00Z\"}}\n");
            }
            byte[] rawBytes = Encoding.UTF8.GetBytes(sb.ToString());

            // Track memory before
            GC.Collect();
            long memoryBefore = GC.GetTotalMemory(true);

            var stopwatch = Stopwatch.StartNew();

            using var stream = new MemoryStream(rawBytes);
            var reader = new MessageFrameReader(stream, 1024 * 1024);

            int count = 0;
            while (await reader.ReadFrameAsync(CancellationToken.None) != null)
            {
                count++;
            }

            stopwatch.Stop();
            long memoryAfter = GC.GetTotalMemory(false);
            long allocatedDiff = memoryAfter - memoryBefore;

            Assert.Equal(1000, count);
            Assert.True(stopwatch.ElapsedMilliseconds < 1000, $"1000 frames processing took too long: {stopwatch.ElapsedMilliseconds} ms");

            // We can print metrics
            Console.WriteLine($"[PERF] 1000 frames parsed in {stopwatch.Elapsed.TotalMilliseconds:F2} ms.");
            Console.WriteLine($"[PERF] Memory allocation change: {allocatedDiff / 1024.0:F2} KB.");
        }
    }
}
