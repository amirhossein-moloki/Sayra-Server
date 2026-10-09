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
    /// Optimized using ReadOnlySpan&lt;byte&gt; and CollectionsMarshal.AsSpan for zero-allocation frame decoding
    /// and SIMD-accelerated newline scanning.
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
            if (length <= 0) return;

            var incomingSpan = new ReadOnlySpan<byte>(data, 0, length);

            lock (_lock)
            {
                if (_buffer.Count + length > _maxMessageSize)
                {
                    ReadOnlySpan<byte> bufSpan = CollectionsMarshal.AsSpan(_buffer);
                    bool hasNewlineInBuf = bufSpan.IndexOf((byte)'\n') >= 0;
                    if (!hasNewlineInBuf)
                    {
                        bool hasNewlineInIncoming = incomingSpan.IndexOf((byte)'\n') >= 0;
                        if (!hasNewlineInIncoming)
                        {
                            throw new InvalidOperationException($"Maximum message frame size ({_maxMessageSize} bytes) exceeded.");
                        }
                    }
                }

                _buffer.AddRange(incomingSpan);
            }
        }

        public List<string> ExtractFrames()
        {
            var frames = new List<string>();
            lock (_lock)
            {
                while (true)
                {
                    ReadOnlySpan<byte> bufSpan = CollectionsMarshal.AsSpan(_buffer);
                    int index = bufSpan.IndexOf((byte)'\n');
                    if (index < 0)
                    {
                        break;
                    }

                    if (index > _maxMessageSize)
                    {
                        _buffer.Clear();
                        throw new InvalidOperationException($"Frame length ({index} bytes) exceeds maximum limit of {_maxMessageSize} bytes.");
                    }

                    // Zero-allocation string decoding directly from memory span slice
                    ReadOnlySpan<byte> frameSpan = bufSpan.Slice(0, index);
                    string frameStr = Encoding.UTF8.GetString(frameSpan).Trim();
                    if (!string.IsNullOrEmpty(frameStr))
                    {
                        frames.Add(frameStr);
                    }

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
