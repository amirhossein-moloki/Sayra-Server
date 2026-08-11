using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Transport;
using Sayra.Backend.Domain.Exceptions;

namespace Sayra.Backend.Infrastructure.Transport
{
    public class MessageFrameReader : IMessageFrameReader
    {
        private readonly Stream _stream;
        private readonly int _maxMessageSizeBytes;
        private readonly List<byte> _buffer = new();
        private readonly byte[] _tempBuffer = new byte[2048];

        public MessageFrameReader(Stream stream, int maxMessageSizeBytes)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _maxMessageSizeBytes = maxMessageSizeBytes > 0 ? maxMessageSizeBytes : throw new ArgumentOutOfRangeException(nameof(maxMessageSizeBytes));
        }

        public async Task<string?> ReadFrameAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                // Check if we already have a complete frame in our buffer
                int newlineIndex = _buffer.IndexOf((byte)'\n');
                if (newlineIndex >= 0)
                {
                    string frame = ExtractFrame(newlineIndex);
                    if (string.IsNullOrWhiteSpace(frame))
                    {
                        // Ignore empty or whitespace-only frames, continue reading
                        continue;
                    }
                    return frame;
                }

                // If buffer itself is already exceeding max size without a newline, reject it early!
                if (_buffer.Count > _maxMessageSizeBytes)
                {
                    _buffer.Clear(); // prevent memory leak / runaway buffer
                    throw new ProtocolException(ProtocolException.FrameTooLarge, "Frame size exceeded maximum limit.");
                }

                // Read more data from stream
                int bytesRead;
                try
                {
                    bytesRead = await _stream.ReadAsync(_tempBuffer, 0, _tempBuffer.Length, cancellationToken);
                }
                catch (IOException ioEx) when (ioEx.InnerException is SocketException)
                {
                    // Socket was closed or reset, treat as EOF
                    return null;
                }
                catch (ObjectDisposedException)
                {
                    // Stream was disposed, treat as EOF
                    return null;
                }

                if (bytesRead == 0)
                {
                    // EOF reached.
                    // If we have some trailing non-newline data, we do not process it because it is an incomplete message
                    // (no incomplete message should be processed before receiving delimiter).
                    _buffer.Clear();
                    return null;
                }

                // Check size limit before adding to buffer
                if (_buffer.Count + bytesRead > _maxMessageSizeBytes)
                {
                    _buffer.Clear(); // prevent memory leak
                    throw new ProtocolException(ProtocolException.FrameTooLarge, "Frame size exceeded maximum limit.");
                }

                // Append newly read bytes to buffer
                for (int i = 0; i < bytesRead; i++)
                {
                    _buffer.Add(_tempBuffer[i]);
                }
            }
        }

        private string ExtractFrame(int newlineIndex)
        {
            byte[] frameBytes = new byte[newlineIndex];
            _buffer.CopyTo(0, frameBytes, 0, newlineIndex);

            // Remove the frame and its '\n' delimiter from the buffer
            _buffer.RemoveRange(0, newlineIndex + 1);

            return Encoding.UTF8.GetString(frameBytes);
        }
    }
}
