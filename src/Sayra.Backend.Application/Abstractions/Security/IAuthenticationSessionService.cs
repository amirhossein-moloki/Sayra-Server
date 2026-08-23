using System;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Application.Abstractions.Security
{
    public interface IAuthenticationSessionService
    {
        Task<AuthenticationSession> CreateSessionAsync(
            Guid? userId,
            Guid? gamerId,
            string? pcId = null,
            Guid? deviceId = null,
            TimeSpan? lifetime = null,
            string? createdBy = null,
            string? ipAddress = null,
            string? userAgent = null,
            CancellationToken cancellationToken = default);

        Task<AuthenticationSession?> GetSessionByTokenAsync(string token, CancellationToken cancellationToken = default);

        Task<bool> ValidateSessionAsync(string token, CancellationToken cancellationToken = default);

        Task<bool> RevokeSessionAsync(string token, string reason, CancellationToken cancellationToken = default);

        Task<int> RevokeAllUserSessionsAsync(Guid userId, string reason, CancellationToken cancellationToken = default);

        Task<int> RevokeAllGamerSessionsAsync(Guid gamerId, string reason, CancellationToken cancellationToken = default);

        Task<int> RevokeAllDeviceSessionsAsync(string pcId, string reason, CancellationToken cancellationToken = default);
    }
}
