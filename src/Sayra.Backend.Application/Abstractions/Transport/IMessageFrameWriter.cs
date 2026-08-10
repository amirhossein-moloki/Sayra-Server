using System.Threading;
using System.Threading.Tasks;

namespace Sayra.Backend.Application.Abstractions.Transport
{
    public interface IMessageFrameWriter
    {
        /// <summary>
        /// Writes a raw string frame with a newline delimiter appended.
        /// </summary>
        Task WriteFrameAsync(string frame, CancellationToken cancellationToken);

        /// <summary>
        /// Serializes the typed message to JSON and writes it as a frame with a newline delimiter.
        /// </summary>
        Task WriteMessageAsync<T>(T message, CancellationToken cancellationToken);
    }
}
