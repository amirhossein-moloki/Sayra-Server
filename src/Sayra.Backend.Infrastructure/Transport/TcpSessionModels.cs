using System;
using System.Collections.Generic;
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
    /// Thread-safe byte buffer accumulator that parses TCP packet segments split by '\n'.
    /// </summary>
    public class TcpFrameParser
    {
        private readonly List<byte> _buffer = new();
        private readonly object _lock = new();

        public void Append(byte[] data, int length)
        {
            lock (_lock)
            {
                for (int i = 0; i < length; i++)
                {
                    _buffer.Add(data[i]);
                }
            }
        }

        public List<string> ExtractFrames()
        {
            var frames = new List<string>();
            lock (_lock)
            {
                int index;
                while ((index = _buffer.IndexOf((byte)'\n')) >= 0)
                {
                    byte[] frameBytes = new byte[index];
                    _buffer.CopyTo(0, frameBytes, 0, index);
                    _buffer.RemoveRange(0, index + 1);

                    string frameStr = Encoding.UTF8.GetString(frameBytes).Trim();
                    if (!string.IsNullOrEmpty(frameStr))
                    {
                        frames.Add(frameStr);
                    }
                }
            }
            return frames;
        }
    }
}
