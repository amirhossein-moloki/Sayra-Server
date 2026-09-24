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
    /// ⚡ OPTIMIZATION: Uses ReadOnlySpan&lt;byte&gt; and CollectionsMarshal.AsSpan for zero-allocation
    /// frame string decoding directly from memory span slices, SIMD-accelerated newline delimiter scanning,
    /// bulk memory block copying (List.AddRange), and O(1) buffer clearing when fully consumed.
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
            if (data == null || length <= 0) return;

            ReadOnlySpan<byte> dataSpan = data.AsSpan(0, length);

            lock (_lock)
            {
                ReadOnlySpan<byte> bufferSpan = CollectionsMarshal.AsSpan(_buffer);

                // Check frame size limit bounds using SIMD-accelerated IndexOf on spans
                if (_buffer.Count + length > _maxMessageSize && bufferSpan.IndexOf((byte)'\n') < 0)
                {
                    bool containsNewlineInNewData = dataSpan.IndexOf((byte)'\n') >= 0;

                    if (!containsNewlineInNewData)
                    {
                        _buffer.Clear();
                        throw new InvalidOperationException($"Maximum message frame size ({_maxMessageSize} bytes) exceeded.");
                    }
                }

                // ⚡ Bulk copy entire span into buffer instead of byte-by-byte loop (avoids per-byte bounds checks and repeated list resizes)
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
                    // ⚡ Use CollectionsMarshal.AsSpan to get direct ReadOnlySpan<byte> view of list backing array
                    ReadOnlySpan<byte> bufferSpan = CollectionsMarshal.AsSpan(_buffer);
                    int index = bufferSpan.IndexOf((byte)'\n');
                    if (index < 0)
                    {
                        break;
                    }

                    if (index > _maxMessageSize)
                    {
                        _buffer.Clear();
                        throw new InvalidOperationException($"Frame length ({index} bytes) exceeds maximum limit of {_maxMessageSize} bytes.");
                    }

                    // ⚡ Decode string directly from span slice without allocating intermediate byte[] heap array
                    ReadOnlySpan<byte> frameSpan = bufferSpan.Slice(0, index);
                    string frameStr = Encoding.UTF8.GetString(frameSpan).Trim();
                    if (!string.IsNullOrEmpty(frameStr))
                    {
                        frames.Add(frameStr);
                    }

                    // ⚡ O(1) buffer reset when all content is consumed, eliminating unnecessary Array.Copy calls
                    if (index + 1 == _buffer.Count)
                    {
                        _buffer.Clear();
                    }
                    else
                    {
                        _buffer.RemoveRange(0, index + 1);
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
