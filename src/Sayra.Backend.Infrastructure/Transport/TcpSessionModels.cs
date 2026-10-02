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

            lock (_lock)
            {
                // Performance optimization: Use ReadOnlySpan for SIMD-accelerated newline scanning and zero-allocation checks
                ReadOnlySpan<byte> incoming = data.AsSpan(0, length);
                ReadOnlySpan<byte> currentBuffer = CollectionsMarshal.AsSpan(_buffer);

                if (_buffer.Count + length > _maxMessageSize && currentBuffer.IndexOf((byte)'\n') < 0)
                {
                    if (incoming.IndexOf((byte)'\n') < 0)
                    {
                        throw new InvalidOperationException($"Maximum message frame size ({_maxMessageSize} bytes) exceeded.");
                    }
                }

                // Performance optimization: Bulk copy bytes into buffer using AddRange instead of byte-by-byte iteration
                _buffer.AddRange(new ArraySegment<byte>(data, 0, length));
            }
        }

        public List<string> ExtractFrames()
        {
            var frames = new List<string>();
            lock (_lock)
            {
                while (_buffer.Count > 0)
                {
                    // Performance optimization: Access internal List buffer as ReadOnlySpan without allocations or copying
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

                    // Performance optimization: Direct string decoding from ReadOnlySpan slice without allocating intermediate byte[] arrays
                    ReadOnlySpan<byte> frameSpan = span.Slice(0, index);
                    string frameStr = Encoding.UTF8.GetString(frameSpan).Trim();
                    if (!string.IsNullOrEmpty(frameStr))
                    {
                        frames.Add(frameStr);
                    }

                    if (index + 1 >= _buffer.Count)
                    {
                        // Performance optimization: Fast O(1) buffer clear when all accumulated bytes are consumed
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
