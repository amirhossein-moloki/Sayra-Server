using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Transport;
using Sayra.Backend.Contracts;

namespace Sayra.Backend.Infrastructure.Transport
{
    public class MessageFrameWriter : IMessageFrameWriter
    {
        private readonly Stream _stream;
        private readonly SemaphoreSlim _writeSemaphore = new(1, 1);

        public MessageFrameWriter(Stream stream)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        }

        public async Task WriteFrameAsync(string frame, CancellationToken cancellationToken)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));

            // Ensure newline is exactly one \n at the end
            if (!frame.EndsWith("\n"))
            {
                frame += "\n";
            }

            byte[] bytes = Encoding.UTF8.GetBytes(frame);

            await _writeSemaphore.WaitAsync(cancellationToken);
            try
            {
                await _stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken);
                await _stream.FlushAsync(cancellationToken);
            }
            finally
            {
                _writeSemaphore.Release();
            }
        }

        public async Task WriteMessageAsync<T>(T message, CancellationToken cancellationToken)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));

            string json = ProtocolSerialization.Serialize(message);
            await WriteFrameAsync(json, cancellationToken);
        }
    }
}
