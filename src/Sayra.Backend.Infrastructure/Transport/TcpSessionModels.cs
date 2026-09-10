using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Sayra.Backend.Infrastructure.Transport
{
    public class ConnectionStateMetadata
    {
        public string ConnectionId { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string? PcId { get; set; }
        public string? Hostname { get; set; }
        public string? SiteId { get; set; }
        public string? ClientVersion { get; set; }
        public DateTime AuthenticatedAt { get; set; }
        public DateTime ConnectedAt { get; set; }
        public DateTime LastActivity { get; set; }
    }

    /// <summary>
    /// Thread-safe byte buffer accumulator that parses TCP packet segments split by '\n',
    /// enforcing maximum message frame size limits for memory protection.
    /// Optimized using ReadOnlySpan and CollectionsMarshal for zero-allocation frame slicing
    /// and SIMD-accelerated newline delimiter scanning.
    /// </summary>
    public class TcpFrameParser
    {
        private readonly List<byte> _buffer = new();
        private readonly object _lock = new();
        private readonly int _maxMessageSize;

        public TcpFrameParser(int maxMessageSize = 65536)
        {
            _maxMessageSize = maxMessageSize > 0 ? maxMessageSize : 65536;
        }

        public void Append(byte[] data, int length)
        {
            if (data == null || length <= 0)
                return;

            lock (_lock)
            {
                ReadOnlySpan<byte> bufferSpan = CollectionsMarshal.AsSpan(_buffer);
                ReadOnlySpan<byte> dataSpan = data.AsSpan(0, length);

                // Check maximum frame size limit when appending bytes without a newline
                if (_buffer.Count + length > _maxMessageSize && bufferSpan.IndexOf((byte)'\n') < 0)
                {
                    if (dataSpan.IndexOf((byte)'\n') < 0)
                    {
                        throw new InvalidOperationException($"Maximum message frame size ({_maxMessageSize} bytes) exceeded.");
                    }
                }

                // Pre-allocate buffer capacity and bulk-append bytes using ReadOnlySpan to avoid element-by-element loop overhead
                _buffer.EnsureCapacity(_buffer.Count + length);
                _buffer.AddRange(dataSpan);
            }
        }

        public List<string> ExtractFrames()
        {
            var frames = new List<string>();
            lock (_lock)
            {
                while (true)
                {
                    ReadOnlySpan<byte> span = CollectionsMarshal.AsSpan(_buffer);
                    int index = span.IndexOf((byte)'\n');
                    if (index < 0)
                    {
                        break;
                    }

                    if (index > _maxMessageSize)
                    {
                        _buffer.Clear();
                        throw new InvalidOperationException($"Frame length ({index} bytes) exceeds maximum limit of {_maxMessageSize} bytes.");
                    }

                    // Decode frame string directly from the span slice to eliminate per-frame byte[] array heap allocation
                    string frameStr = Encoding.UTF8.GetString(span.Slice(0, index)).Trim();

                    // Perform O(1) buffer reset when all bytes are consumed, or remove consumed frame bytes
                    if (index + 1 == _buffer.Count)
                    {
                        _buffer.Clear();
                    }
                    else
                    {
                        _buffer.RemoveRange(0, index + 1);
                    }

                    if (!string.IsNullOrEmpty(frameStr))
                    {
                        frames.Add(frameStr);
                    }
                }

                if (_buffer.Count > _maxMessageSize)
                {
                    _buffer.Clear();
                    throw new InvalidOperationException($"Accumulated frame buffer ({_buffer.Count} bytes) exceeds maximum limit of {_maxMessageSize} bytes.");
                }
            }
            return frames;
        }
    }
}
