using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sayra.Backend.Application.Abstractions.Security
{
    public interface ILoginProtectionService
    {
        Task<bool> IsLockedOutAsync(string usernameOrIp, CancellationToken cancellationToken = default);

        Task RecordFailedAttemptAsync(
            string usernameOrIp,
            Guid? userId = null,
            string? ipAddress = null,
            string? deviceId = null,
            string? failureReason = null,
            CancellationToken cancellationToken = default);

        Task ResetAttemptsAsync(string usernameOrIp, Guid? userId = null, CancellationToken cancellationToken = default);

        Task UnlockAsync(string usernameOrIp, CancellationToken cancellationToken = default);
    }
}
