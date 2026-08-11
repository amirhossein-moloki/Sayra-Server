using System.Threading;
using System.Threading.Tasks;

namespace Sayra.Backend.Application.Abstractions.Transport
{
    public interface IMessageFrameReader
    {
        /// <summary>
        /// Reads a single message frame from the stream.
        /// Returns null on EOF or when stream closes.
        /// </summary>
        Task<string?> ReadFrameAsync(CancellationToken cancellationToken);
    }
}
