using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Transport;

namespace Sayra.Backend.Application.Abstractions.Security
{
    public interface ITcpAuthenticationService
    {
        /// <summary>
        /// Orchestrates the secure TCP challenge-response handshake with a client over TLS 1.3.
        /// </summary>
        /// <param name="connection">The TCP connection context.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if authentication succeeded; false otherwise.</returns>
        Task<bool> AuthenticateAsync(ITcpConnection connection, CancellationToken cancellationToken);
    }
}
