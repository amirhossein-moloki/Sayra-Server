using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Transport;

namespace Sayra.Backend.Application.Abstractions.Security
{
    public interface IClientAuthenticationService
    {
        /// <summary>
        /// Generates a cryptographically secure challenge and stores it inside the connection session.
        /// </summary>
        Task<string> GenerateChallengeAsync(ITcpConnection connection, CancellationToken cancellationToken = default);

        /// <summary>
        /// Validates the challenge response, verifies the HMAC, extracts/decrypts the session key, and transitions state.
        /// </summary>
        Task<AuthenticationResult> ValidateResponseAsync(ITcpConnection connection, AuthResponseDto response, CancellationToken cancellationToken = default);

        /// <summary>
        /// Cleans up any tracking or resources for the connection session.
        /// </summary>
        void CleanupSession(string connectionId);
    }
}
